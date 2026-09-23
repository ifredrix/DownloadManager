using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent.Client;

namespace IfredrixDownloadManager;

/// <summary>
/// Orchestrates every download. HTTP(S) transfers run through
/// <see cref="HttpDownloader"/>, BitTorrent transfers through
/// <see cref="TorrentEngine"/>, so both downloaders share one queue,
/// one UI and one speed limiter.
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellers = new();
    private readonly ConcurrentDictionary<string, SpeedSample> _samples = new();
    private readonly ConcurrentDictionary<string, Task> _runs = new();
    private readonly ConcurrentDictionary<string, int> _retries = new();

    /// <summary>
    /// Live per-connection snapshot for the progress window. Always returns at
    /// least one aggregate row so callers never special-case unknown sizes.
    /// </summary>
    public List<SegmentStat> GetConnectionStats(string taskId)
    {
        var empty = new List<SegmentStat>();
        var task = GetTask(taskId);
        if (task == null) return empty;

        var downloading = task.Status == DownloadStatus.Downloading;
        if (task.Type == DownloadType.Torrent || task.Type == DownloadType.Stream)
        {
            var length = task.Type == DownloadType.Stream ? 100 : task.FileSize;
            var done = task.Type == DownloadType.Stream
                ? Math.Clamp(task.DownloadedBytes, 0, 100)
                : task.DownloadedBytes;
            return new List<SegmentStat>
            {
                new() { Index = 0, Start = 0, Length = length, Downloaded = done, Active = downloading }
            };
        }

        if (FtpDownloader.IsFtpUrl(task.Url))
        {
            var ftp = _ftp.GetSegments(taskId);
            if (ftp.Count > 0) return ftp;
        }
        else
        {
            var http = _http.GetSegments(taskId);
            if (http.Count > 0) return http;
        }

        return new List<SegmentStat>
        {
            new() { Index = 0, Start = 0, Length = task.FileSize, Downloaded = task.DownloadedBytes, Active = downloading }
        };
    }
    private readonly RateLimiter _limiter = new();
    private readonly HttpDownloader _http;
    private readonly FtpDownloader _ftp;
    private readonly TorrentEngine _torrents;
    private readonly StreamCapture _streams = new();
    private readonly System.Threading.Timer _statsTimer;
    private int _maxConcurrent = 3;
    private int _connections = DefaultConnections;
    private int _pumping;
    private bool _disposed;

    public event EventHandler<DownloadTask>? TaskAdded;
    public event EventHandler<DownloadTask>? TaskUpdated;
    public event EventHandler<DownloadTask>? TaskCompleted;
    public event EventHandler<DownloadTask>? TaskFailed;
    public event EventHandler<DownloadTask>? TaskRemoved;

    public DownloadManager(string downloadPath, int maxConcurrent = 3)
    {
        DownloadPath = downloadPath;
        _maxConcurrent = Math.Max(1, maxConcurrent);

        Directory.CreateDirectory(DownloadPath);

        _http = new HttpDownloader(_limiter);
        _ftp = new FtpDownloader(_limiter);
        _torrents = new TorrentEngine(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ifredrixDownloadManager", "cache"));

        _statsTimer = new System.Threading.Timer(_ => StatsTick(), null, 500, 500);
    }

    public string DownloadPath { get; set; }

    public int MaxConcurrentDownloads
    {
        get => _maxConcurrent;
        set => _maxConcurrent = Math.Max(1, value);
    }

    /// <summary>Hard ceiling for one download, mirrors HttpDownloader.MaxConnections.</summary>
    public const int MaxConnectionsPerDownload = 32;

    /// <summary>
    /// Default: 20 parallel connections. 16-20 is the sweet spot: it is high enough
    /// to defeat per-connection throttling (archive.org served 43 KB/s on one
    /// connection but ~6 MB/s on 16) while staying under most per-IP limits.
    /// Raise towards <see cref="MaxConnectionsPerDownload"/> for throttled servers.
    /// </summary>
    public const int DefaultConnections = 20;

    public int ConnectionsPerDownload
    {
        get => _connections;
        set => _connections = Math.Clamp(value, 1, MaxConnectionsPerDownload);
    }

    public long SpeedLimitBytesPerSecond
    {
        get => _limiter.LimitBps;
        set
        {
            _limiter.LimitBps = value;
            _torrents.SetSpeedLimit(value, UploadLimitBytesPerSecond);
        }
    }

    public long UploadLimitBytesPerSecond { get; set; }

    /// <summary>When true, every HTTP(S) download is routed via Tor.</summary>
    public bool RouteAllViaTor { get; set; }

    /// <summary>Per-category save folders (empty = main download path).</summary>
    public Dictionary<string, string> CategoryDirs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Save folder for a category, falling back to the main path.</summary>
    public string DirForCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            CategoryDirs.TryGetValue(category, out var dir) &&
            !string.IsNullOrWhiteSpace(dir))
        {
            return dir;
        }
        return DownloadPath;
    }

    /// <summary>Where stream downloads live (own subfolder under its base).</summary>
    public string StreamsDirectory =>
        Path.Combine(DirForCategory("Streams"), "Streams");

    public IEnumerable<DownloadTask> GetAllTasks() => _tasks.Values.OrderBy(t => t.CreatedAt).ToList();

    public DownloadTask? GetTask(string taskId) => _tasks.TryGetValue(taskId, out var task) ? task : null;

    // ---------------------------------------------------------------- add ----

    public Task<DownloadTask> AddDownloadAsync(string source, DownloadType type = DownloadType.Regular)
        => AddDownloadCoreAsync(source, type, forceTor: false);

    private async Task<DownloadTask> AddDownloadCoreAsync(
        string source, DownloadType type, bool forceTor)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Nothing to download.", nameof(source));
        }

        source = source.Trim();

        // MMS is long dead as a wire protocol; the servers that still answer
        // mms:// do MMS-over-HTTP, so rewrite and download as regular HTTP.
        if (source.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
        {
            source = "http://" + source["mms://".Length..];
        }

        var task = new DownloadTask { Url = source, Type = type };

        if (type == DownloadType.Regular)
        {
            var probed = await ProbeDownloadAsync(source, forceTor).ConfigureAwait(false);
            return await AddProbedAsync(probed).ConfigureAwait(false);
        }
        else if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var (name, size) = TorrentEngine.InspectMagnet(source);
            var folder = HttpDownloader.SanitizeFileName(
                string.IsNullOrWhiteSpace(name) ? "magnet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") : name);

            task.FileName = folder;
            task.FileSize = size ?? 0;
            task.SupportsRange = true;
            task.Category = "Torrent";
            task.SavePath = UniqueDirectory(Path.Combine(DirForCategory("Torrent"), folder));
            task.FileName = Path.GetFileName(task.SavePath);
        }
        else
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Torrent file not found.", source);
            }

            var (name, size) = await TorrentEngine.InspectAsync(source).ConfigureAwait(false);
            var folder = HttpDownloader.SanitizeFileName(
                string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(source) : name);

            task.FileName = folder;
            task.FileSize = size;
            task.SupportsRange = true;
            task.Category = "Torrent";
            task.SavePath = UniqueDirectory(Path.Combine(DirForCategory("Torrent"), folder));
            task.FileName = Path.GetFileName(task.SavePath);
        }

        task.Status = DownloadStatus.Pending;
        _tasks[task.Id] = task;
        TaskAdded?.Invoke(this, task);

        await PumpAsync().ConfigureAwait(false);
        return task;
    }

    /// <summary>Probe result shown in the New Download dialog.</summary>
    public sealed class ProbedDownload
    {
        public string Source { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool SupportsRange { get; set; }
        public bool UseTor { get; set; }
        public bool IsFtp { get; set; }
        public string Category { get; set; } = "Other";
        public string DefaultDir { get; set; } = string.Empty;
        /// <summary>True when the user picked the folder explicitly.</summary>
        public bool CustomDir { get; set; }
    }

    /// <summary>Probes a regular link without queueing (New Download dialog).</summary>
    public async Task<ProbedDownload> ProbeDownloadAsync(string source, bool forceTor = false)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Nothing to download.", nameof(source));
        }

        source = source.Trim();
        if (source.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
        {
            source = "http://" + source["mms://".Length..];
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeFtp && uri.Scheme != "ftps"))
        {
            throw new ArgumentException("Only http://, https://, ftp:// and ftps:// links are supported.");
        }

        var routeTor = forceTor || RouteAllViaTor;
        var probed = new ProbedDownload
        {
            Source = source,
            UseTor = routeTor || TorProxy.IsOnionUrl(source),
            IsFtp = uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == "ftps"
        };

        if (probed.IsFtp)
        {
            FtpProbeResult? fp = null;
            for (var attempt = 1; ; attempt++)
            {
                fp = await _ftp.ProbeAsync(source, useTor: routeTor).ConfigureAwait(false);
                if (fp.Ok || attempt >= 3) break;
                await Task.Delay(1000 * attempt).ConfigureAwait(false);
            }
            if (!fp.Ok)
            {
                throw new InvalidOperationException(fp.Error ?? "The FTP server refused the request.");
            }
            probed.FileName = HttpDownloader.SanitizeFileName(
                fp.SuggestedFileName ?? FtpDownloader.DeriveFileName(source));
            probed.FileSize = fp.TotalSize;
            probed.SupportsRange = fp.SupportsRange;
        }
        else
        {
            var probe = await ProbeWithRetryAsync(source, routeTor).ConfigureAwait(false);
            if (!probe.Ok)
            {
                throw new InvalidOperationException(probe.Error ?? "The server refused the request.");
            }
            probed.FileName = HttpDownloader.SanitizeFileName(
                probe.SuggestedFileName ?? DeriveFileName(source));
            probed.FileSize = probe.TotalSize;
            probed.SupportsRange = probe.SupportsRange;
        }

        probed.Category = GetCategory(probed.FileName);
        probed.DefaultDir = DirForCategory(probed.Category);
        return probed;
    }

    /// <summary>Queues a probed (possibly dialog-edited) regular download.</summary>
    public async Task<DownloadTask> AddProbedAsync(ProbedDownload probed)
    {
        if (probed == null) throw new ArgumentNullException(nameof(probed));

        var dir = string.IsNullOrWhiteSpace(probed.DefaultDir) ? DownloadPath : probed.DefaultDir;
        Directory.CreateDirectory(dir);

        var fileName = HttpDownloader.SanitizeFileName(
            string.IsNullOrWhiteSpace(probed.FileName) ? DeriveFileName(probed.Source) : probed.FileName);

        var task = new DownloadTask
        {
            Url = probed.Source,
            Type = DownloadType.Regular,
            FileName = fileName,
            FileSize = Math.Max(0, probed.FileSize),
            SupportsRange = probed.SupportsRange,
            UseTor = probed.UseTor,
            Category = GetCategory(fileName)
        };
        var baseDir = probed.CustomDir && !string.IsNullOrWhiteSpace(probed.DefaultDir)
            ? probed.DefaultDir
            : DirForCategory(task.Category);
        Directory.CreateDirectory(baseDir);
        task.SavePath = UniqueFilePath(Path.Combine(baseDir, fileName));
        task.FileName = Path.GetFileName(task.SavePath);

        task.Status = DownloadStatus.Pending;
        _tasks[task.Id] = task;
        TaskAdded?.Invoke(this, task);

        await PumpAsync().ConfigureAwait(false);
        return task;
    }

    /// <summary>
    /// Queues a stream URL (YouTube etc.) for background download of the best
    /// MP4 via yt-dlp. No format dialog; progress flows through TaskUpdated.
    /// </summary>
    public async Task<DownloadTask> AddStreamAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Nothing to download.", nameof(url));
        }

        url = url.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Only http:// and https:// stream links are supported.");
        }

        if (_streams.ResolveTool() == null)
        {
            throw new FileNotFoundException(
                "yt-dlp is not available yet. Add any stream link once in the app to auto-fetch it.");
        }

        var baseName = HttpDownloader.SanitizeFileName(
            "stream-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var task = new DownloadTask
        {
            Url = url,
            Type = DownloadType.Stream,
            FileName = baseName + " (resolving...)",
            FileSize = 0,
            SupportsRange = false,
            SavePath = UniqueDirectory(Path.Combine(StreamsDirectory, baseName)),
            Category = "Video",
            Status = DownloadStatus.Pending
        };

        _tasks[task.Id] = task;
        TaskAdded?.Invoke(this, task);

        await PumpAsync().ConfigureAwait(false);
        return task;
    }

    /// <summary>
    /// Queues a regular HTTP(S) download forced through the Tor network
    /// (censorship circumvention / anonymity for clearnet hosts).
    /// </summary>
    public Task<DownloadTask> AddTorDownloadAsync(string source)
        => AddDownloadCoreAsync(source, DownloadType.Regular, forceTor: true);

    /// <summary>Probes with retries for flaky servers (mirror behaviour).</summary>
    private async Task<ProbeResult> ProbeWithRetryAsync(string source, bool routeTor)
    {
        for (var attempt = 1; ; attempt++)
        {
            var probe = await _http.ProbeAsync(source, useTor: routeTor).ConfigureAwait(false);
            if (probe.Ok || attempt >= 3) return probe;
            await Task.Delay(1000 * attempt).ConfigureAwait(false);
        }
    }

    /// <summary>Rebuilds HTTP clients after proxy/connection changes.</summary>
    public void ReloadConnection() => _http.ReloadConnection();

    /// <summary>
    /// Re-probes a stalled task's address (refresh): updates size/range info
    /// and clears a stale error without touching downloaded bytes.
    /// </summary>
    public async Task<string> RefreshTaskAsync(string taskId)
    {
        var task = GetTask(taskId)
                   ?? throw new ArgumentException("Download not found.", nameof(taskId));
        if (task.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            throw new InvalidOperationException("Pause the download before refreshing its address.");
        }
        if (task.Type != DownloadType.Regular)
        {
            throw new InvalidOperationException("Refresh applies to HTTP/FTP downloads only.");
        }

        if (FtpDownloader.IsFtpUrl(task.Url))
        {
            var ftp = new FtpDownloader(_limiter);
            var f = await ftp.ProbeAsync(task.Url, task.UseTor).ConfigureAwait(false);
            if (!f.Ok) throw new InvalidOperationException(f.Error ?? "The server refused the request.");
            if (task.DownloadedBytes == 0)
            {
                task.FileSize = f.TotalSize;
                task.SupportsRange = f.SupportsRange;
            }
        }
        else
        {
            var probe = await _http.ProbeAsync(task.Url, task.UseTor).ConfigureAwait(false);
            if (!probe.Ok) throw new InvalidOperationException(probe.Error ?? "The server refused the request.");
            if (task.DownloadedBytes == 0)
            {
                task.FileSize = probe.TotalSize;
                task.SupportsRange = probe.SupportsRange;
            }
        }

        _retries.TryRemove(taskId, out _);
        task.ErrorMessage = string.Empty;
        TaskUpdated?.Invoke(this, task);
        return "Address refreshed.";
    }

    /// <summary>Sets a per-download speed cap (bytes/s, 0 = global limit).</summary>
    public bool SetTaskSpeedLimit(string taskId, long bytesPerSecond)
    {
        var task = GetTask(taskId);
        if (task == null) return false;
        task.SpeedLimitBps = bytesPerSecond;
        return true;
    }

    /// <summary>
    /// Flips Tor routing for one download. Active transfers restart on the new
    /// route; downloaded bytes are kept via the resume sidecar.
    /// </summary>
    public async Task<bool> SetUseTorAsync(string taskId, bool useTor)
    {
        var task = GetTask(taskId);
        if (task == null) return false;
        if (task.Type != DownloadType.Regular) return false;
        if (task.UseTor == useTor) return true;

        var wasActive = task.Status is DownloadStatus.Downloading or DownloadStatus.Queued;
        if (wasActive) PauseDownload(taskId);

        task.UseTor = useTor;
        _retries.TryRemove(taskId, out _);
        TaskUpdated?.Invoke(this, task);

        if (wasActive) await StartDownloadAsync(taskId).ConfigureAwait(false);
        return true;
    }

    /// <summary>Exports the queue (same format as history) for backup/transfer.</summary>
    public void ExportQueue(string file) => SaveHistory(file);

    /// <summary>
    /// Imports a queue file. Completion records without files on disk come
    /// back as Paused so they can be fetched again.
    /// </summary>
    public int ImportQueue(string file)
    {
        try
        {
            if (!File.Exists(file)) return 0;
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<HistoryEntry>>(
                File.ReadAllText(file));
            if (entries == null) return 0;

            var existing = new HashSet<string>(
                _tasks.Values.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Url) || existing.Contains(e.Url.Trim())) continue;

                var status = e.Status;
                if (status is DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Pending)
                {
                    status = DownloadStatus.Paused;
                }
                var savePath = string.IsNullOrWhiteSpace(e.SavePath)
                    ? Path.Combine(DownloadPath, string.IsNullOrWhiteSpace(e.FileName)
                        ? DeriveFileName(e.Url) : e.FileName)
                    : e.SavePath;
                if (status == DownloadStatus.Completed &&
                    !File.Exists(savePath) && !Directory.Exists(savePath))
                {
                    status = DownloadStatus.Paused;
                }

                var task = new DownloadTask
                {
                    Url = e.Url.Trim(),
                    FileName = string.IsNullOrWhiteSpace(e.FileName) ? DeriveFileName(e.Url) : e.FileName,
                    SavePath = savePath,
                    FileSize = Math.Max(0, e.FileSize),
                    Status = status,
                    Type = e.Type,
                    Category = string.IsNullOrWhiteSpace(e.Category) ? "Other" : e.Category,
                    UseTor = e.UseTor,
                    SupportsRange = e.SupportsRange,
                    CreatedAt = e.CreatedAt == default ? DateTime.Now : e.CreatedAt
                };
                task.DownloadedBytes = Math.Clamp(e.DownloadedBytes, 0, Math.Max(0, e.FileSize));
                task.SpeedLimitBps = Math.Max(0, e.SpeedLimitBps);
                task.OpenFolderOnDone = e.OpenFolderOnDone;
                if (!string.IsNullOrWhiteSpace(e.Id) && !_tasks.ContainsKey(e.Id)) task.Id = e.Id;

                _tasks[task.Id] = task;
                existing.Add(task.Url);
                TaskAdded?.Invoke(this, task);
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    // ------------------------------------------------------------- control ----

    public async Task<bool> StartDownloadAsync(string taskId)
    {
        var task = GetTask(taskId);
        if (task == null) return false;
        if (task.Status is DownloadStatus.Completed or DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            return false;
        }

        if (task.Status == DownloadStatus.Error) task.ErrorMessage = string.Empty;
        _retries.TryRemove(taskId, out _);
        task.Status = DownloadStatus.Pending;
        TaskUpdated?.Invoke(this, task);

        await PumpAsync().ConfigureAwait(false);
        return true;
    }

    public bool PauseDownload(string taskId)
    {
        var task = GetTask(taskId);
        if (task == null) return false;
        if (task.Status is not (DownloadStatus.Downloading or DownloadStatus.Queued)) return false;

        CancelToken(taskId);
        task.Status = DownloadStatus.Paused;
        ResetSpeed(task);
        TaskUpdated?.Invoke(this, task);

        if (task.Type == DownloadType.Torrent)
        {
            _ = _torrents.PauseAsync(taskId);
        }

        return true;
    }

    public bool ResumeDownload(string taskId)
    {
        var task = GetTask(taskId);
        if (task == null) return false;

        _retries.TryRemove(taskId, out _);
        task.Status = DownloadStatus.Pending;
        TaskUpdated?.Invoke(this, task);
        _ = PumpAsync();
        return true;
    }

    public bool CancelDownload(string taskId)
    {
        var task = GetTask(taskId);
        if (task == null) return false;

        CancelToken(taskId);
        task.Status = DownloadStatus.Cancelled;
        ResetSpeed(task);
        _samples.TryRemove(taskId, out _);
        TaskLimits.Drop(taskId);

        if (task.Type == DownloadType.Torrent)
        {
            _ = _torrents.RemoveAsync(taskId, RemoveMode.CacheDataAndDownloadedData);
        }
        else
        {
            TryDeleteFile(task.SavePath + ".tdpart");
            // The target file was created by us (the path was made unique on add),
            // so removing it on cancel is safe.
            TryDeleteFile(task.SavePath);
        }

        TaskUpdated?.Invoke(this, task);
        return true;
    }

    public bool RemoveTask(string taskId)
    {
        var task = GetTask(taskId);
        if (task == null) return false;

        if (task.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            CancelDownload(taskId);
        }

        _tasks.TryRemove(taskId, out _);
        TaskLimits.Drop(taskId);
        _http.DropPlan(taskId);
        _ftp.DropPlan(taskId);
        if (task.Type == DownloadType.Torrent)
        {
            _ = _torrents.RemoveAsync(taskId, RemoveMode.DownloadedDataOnly);
        }

        TaskRemoved?.Invoke(this, task);
        return true;
    }

    // ---------------------------------------------------------------- pump ----

    private async Task PumpAsync()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _pumping, 1) == 1) return;

        try
        {
            while (!_disposed)
            {
                var active = _tasks.Values.Count(t =>
                    t.Status is DownloadStatus.Downloading or DownloadStatus.Queued);
                if (active >= _maxConcurrent) break;

                var next = _tasks.Values
                    .Where(t => t.Status == DownloadStatus.Pending)
                    .OrderBy(t => t.CreatedAt)
                    .FirstOrDefault();
                if (next == null) break;

                next.Status = DownloadStatus.Queued;
                TaskUpdated?.Invoke(this, next);
                _ = EnqueueAsync(next);

                await Task.Delay(30).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pumping, 0);
        }
    }

    /// <summary>
    /// Chains a new run behind any run that is still winding down, so a quick
    /// pause -> resume can never have two transfers writing the same file.
    /// </summary>
    private Task EnqueueAsync(DownloadTask task)
    {
        var previous = _runs.TryGetValue(task.Id, out var pending) ? pending : Task.CompletedTask;
        var next = previous
            .ContinueWith(_ => RunAsync(task), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default)
            .Unwrap();
        _runs[task.Id] = next;
        return next;
    }

    private async Task RunAsync(DownloadTask task)
    {
        var cts = new CancellationTokenSource();
        _cancellers[task.Id] = cts;
        _samples[task.Id] = new SpeedSample(task.DownloadedBytes, DateTime.UtcNow);

        try
        {
            task.Status = DownloadStatus.Downloading;
            task.ErrorMessage = string.Empty;
            TaskUpdated?.Invoke(this, task);

            if (task.Type == DownloadType.Regular)
            {
                if (FtpDownloader.IsFtpUrl(task.Url))
                {
                    await _ftp.DownloadAsync(task, _connections, cts.Token).ConfigureAwait(false);
                }
                else
                {
                    await _http.DownloadAsync(task, _connections, cts.Token).ConfigureAwait(false);
                }
            }
            else if (task.Type == DownloadType.Stream)
            {
                await RunStreamAsync(task, cts.Token).ConfigureAwait(false);
            }
            else
            {
                await _torrents.DownloadAsync(task, cts.Token).ConfigureAwait(false);
            }

            if (!cts.IsCancellationRequested && task.Status == DownloadStatus.Downloading)
            {
                Complete(task);
            }
        }
        catch (OperationCanceledException)
        {
            // Paused or cancelled on purpose - status was already updated.
        }
        catch (Exception ex)
        {
            if (task.Status is not (DownloadStatus.Cancelled or DownloadStatus.Paused))
            {
                // Transient network failures get silent retries with backoff
                // (mirror/refresh behaviour) before the task really fails.
                var attempt = _retries.AddOrUpdate(task.Id, 1, (_, n) => n + 1);
                if (attempt <= 3 && task.Type != DownloadType.Torrent && IsTransient(ex))
                {
                    task.Status = DownloadStatus.Pending;
                    task.ErrorMessage = $"Retrying ({attempt}/3): {ex.Message}";
                    ResetSpeed(task);
                    TaskUpdated?.Invoke(this, task);
                    try { await Task.Delay(2000 * attempt).ConfigureAwait(false); }
                    catch { /* shutting down */ }
                }
                else
                {
                    _retries.TryRemove(task.Id, out _);
                    task.Status = DownloadStatus.Error;
                    task.ErrorMessage = ex.Message;
                    ResetSpeed(task);
                    // Stop the sibling chunk/peer loops immediately instead of letting
                    // them keep writing into a file we are about to abandon.
                    try { cts.Cancel(); } catch { /* ignore */ }
                    TaskFailed?.Invoke(this, task);
                }
            }
        }
        finally
        {
            _cancellers.TryRemove(task.Id, out _);
            cts.Dispose();
            _ = PumpAsync();
        }
    }

    private async Task RunStreamAsync(DownloadTask task, CancellationToken ct)
    {
        Directory.CreateDirectory(task.SavePath);

        // Best-effort title so the row shows something meaningful.
        try
        {
            var title = await _streams.GetTitleAsync(task.Url, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(title))
            {
                task.FileName = HttpDownloader.SanitizeFileName(title.Trim()) + ".mp4";
                TaskUpdated?.Invoke(this, task);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Keep the placeholder name; the download itself is authoritative.
        }

        // Map 0-100% onto FileSize=100 so the grid progress bar moves even
        // though the true byte total is unknown until yt-dlp finishes.
        task.FileSize = 100;
        task.DownloadedBytes = 0;
        var progress = new Progress<StreamProgress>(p =>
        {
            task.DownloadedBytes = Math.Clamp((long)Math.Round(p.Percent), 0, 100);
            TaskUpdated?.Invoke(this, task);
        });

        var savedPath = (string?)null;
        try
        {
            savedPath = await _streams.DownloadBestAsync(task.Url, task.SavePath, progress, ct, task.SpeedLimitBps)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                  ex.Message.Contains("merge", StringComparison.OrdinalIgnoreCase))
        {
            // Merge/TS-remux needs ffmpeg: fetch the bundled copy once and
            // retry the full-quality download before falling back.
            task.FileName += " (fetching ffmpeg...)";
            TaskUpdated?.Invoke(this, task);
            try
            {
                await _streams.EnsureFfmpegAsync(null, ct).ConfigureAwait(false);
                task.DownloadedBytes = 0;
                savedPath = await _streams.DownloadBestAsync(task.Url, task.SavePath, progress, ct, task.SpeedLimitBps)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // No ffmpeg on this machine: retry as a single MP4 file (no merge).
                task.DownloadedBytes = 0;
                savedPath = await _streams.DownloadAsync(
                    task.Url, StreamCapture.FallbackMp4Format, task.SavePath, progress, ct, task.SpeedLimitBps)
                    .ConfigureAwait(false);
            }
        }

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

        if (string.IsNullOrEmpty(savedPath))
        {
            throw new InvalidOperationException("yt-dlp finished but no output file was found.");
        }

        var fi = new FileInfo(savedPath);
        task.FileName = fi.Name;
        task.SavePath = savedPath;
        task.FileSize = fi.Length;
        task.DownloadedBytes = fi.Length;
        task.Category = GetCategory(fi.Name);
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException || ex is TimeoutException || ex is IOException;

    private void Complete(DownloadTask task)
    {
        _retries.TryRemove(task.Id, out _);
        TaskLimits.Drop(task.Id);
        task.Status = DownloadStatus.Completed;
        task.CompletedAt = DateTime.Now;
        if (task.FileSize > 0) task.DownloadedBytes = task.FileSize;
        ResetSpeed(task);
        TryDeleteFile(task.SavePath + ".tdpart");
        TaskCompleted?.Invoke(this, task);
    }

    // --------------------------------------------------------------- stats ----

    private void StatsTick()
    {
        if (_disposed) return;

        try
        {
            var now = DateTime.UtcNow;
            foreach (var task in _tasks.Values.ToList())
            {
                if (task.Status != DownloadStatus.Downloading)
                {
                    if (task.DownloadSpeed != 0 || task.UploadSpeed != 0)
                    {
                        ResetSpeed(task);
                    }
                    continue;
                }

                if (task.Type == DownloadType.Torrent)
                {
                    _torrents.UpdateStats(task);
                }
                else if (task.Type == DownloadType.Stream)
                {
                    // Progress is pushed by the yt-dlp callback; just repaint.
                }
                else
                {
                    var current = task.DownloadedBytes;
                    var sample = _samples.GetOrAdd(task.Id, _ => new SpeedSample(current, now));
                    var elapsed = (now - sample.Time).TotalSeconds;
                    if (elapsed >= 0.25)
                    {
                        var instant = Math.Max(0, (current - sample.Bytes) / elapsed);
                        task.DownloadSpeed = task.DownloadSpeed <= 0
                            ? instant
                            : task.DownloadSpeed * 0.6 + instant * 0.4;
                        sample.Bytes = current;
                        sample.Time = now;
                    }

                    var remaining = task.FileSize - current;
                    task.TimeRemaining = task.DownloadSpeed > 1024 && remaining > 0
                        ? TimeSpan.FromSeconds(remaining / task.DownloadSpeed)
                        : null;
                }

                TaskUpdated?.Invoke(this, task);
            }
        }
        catch
        {
            // A failed stats tick must never kill the timer.
        }
    }

    private static void ResetSpeed(DownloadTask task)
    {
        task.DownloadSpeed = 0;
        task.UploadSpeed = 0;
        task.TimeRemaining = null;
    }

    private void CancelToken(string taskId)
    {
        if (_cancellers.TryGetValue(taskId, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        }
    }

    // -------------------------------------------------------------- history ----

    /// <summary>
    /// Persists the task list so downloads survive an app restart.
    /// Incomplete tasks resume from sidecar / fast-resume data.
    /// </summary>
    public void SaveHistory(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var entries = _tasks.Values
                .OrderBy(t => t.CreatedAt)
                .Select(t => new HistoryEntry
                {
                    Id = t.Id,
                    Url = t.Url,
                    FileName = t.FileName,
                    SavePath = t.SavePath,
                    FileSize = t.FileSize,
                    DownloadedBytes = t.DownloadedBytes,
                    Status = t.Status switch
                    {
                        DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Pending
                            => DownloadStatus.Paused,
                        var s => s
                    },
                    Type = t.Type,
                    Category = t.Category,
                    UseTor = t.UseTor,
                    SupportsRange = t.SupportsRange,
                    SpeedLimitBps = t.SpeedLimitBps,
                    OpenFolderOnDone = t.OpenFolderOnDone,
                    CreatedAt = t.CreatedAt
                })
                .ToList();
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(entries));
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>Restores tasks saved by <see cref="SaveHistory"/> (all idle).</summary>
    public int LoadHistory(string file)
    {
        try
        {
            if (!File.Exists(file)) return 0;
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<HistoryEntry>>(
                File.ReadAllText(file));
            if (entries == null) return 0;

            var count = 0;
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Url)) continue;
                var task = new DownloadTask
                {
                    Url = e.Url,
                    FileName = string.IsNullOrWhiteSpace(e.FileName) ? DeriveFileName(e.Url) : e.FileName,
                    SavePath = e.SavePath,
                    FileSize = Math.Max(0, e.FileSize),
                    Status = e.Status,
                    Type = e.Type,
                    Category = string.IsNullOrWhiteSpace(e.Category) ? "Other" : e.Category,
                    UseTor = e.UseTor,
                    SupportsRange = e.SupportsRange,
                    CreatedAt = e.CreatedAt == default ? DateTime.Now : e.CreatedAt
                };
                task.SpeedLimitBps = Math.Max(0, e.SpeedLimitBps);
                task.OpenFolderOnDone = e.OpenFolderOnDone;
                task.DownloadedBytes = Math.Clamp(e.DownloadedBytes, 0, Math.Max(0, e.FileSize));
                if (!string.IsNullOrWhiteSpace(e.Id) && !_tasks.ContainsKey(e.Id))
                {
                    task.Id = e.Id;
                }
                if (string.IsNullOrWhiteSpace(task.SavePath))
                {
                    task.SavePath = Path.Combine(DownloadPath, task.FileName);
                }
                _tasks[task.Id] = task;
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class HistoryEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public long DownloadedBytes { get; set; }
        public DownloadStatus Status { get; set; }
        public DownloadType Type { get; set; }
        public string Category { get; set; } = string.Empty;
        public bool UseTor { get; set; }
        public bool SupportsRange { get; set; }
        public long SpeedLimitBps { get; set; }
        public bool OpenFolderOnDone { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // -------------------------------------------------------------- helpers ----

    public string GetCategory(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" => "Video",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "Audio",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" => "Archive",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" => "Document",
            ".exe" or ".msi" or ".apk" or ".dmg" or ".deb" or ".rpm" => "Application",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "Image",
            _ => "Other"
        };
    }

    /// <summary>
    /// Inserts a row into the grid for a file that already exists on disk
    /// (e.g. produced by yt-dlp) so the user can find/track it.
    /// </summary>
    public DownloadTask RecordCompleted(string savePath, string url, string sourceLabel = "stream")
    {
        var fi = new FileInfo(savePath);
        var task = new DownloadTask
        {
            Url = url,
            Type = DownloadType.Regular,
            FileName = fi.Name,
            SavePath = savePath,
            FileSize = fi.Exists ? fi.Length : 0,
            DownloadedBytes = fi.Exists ? fi.Length : 0,
            Status = DownloadStatus.Completed,
            CompletedAt = DateTime.Now,
            Category = GetCategory(fi.Name)
        };
        _tasks[task.Id] = task;
        TaskAdded?.Invoke(this, task);
        return task;
    }

    public static string DeriveFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch
        {
            // ignore
        }

        return "download-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    public static string UniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({counter}){extension}");
            counter++;
        } while (File.Exists(candidate) || File.Exists(candidate + ".tdpart"));

        return candidate;
    }

    private static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path)) return path;

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);

        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(parent, $"{name} ({counter})");
            counter++;
        } while (Directory.Exists(candidate));

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _statsTimer.Dispose(); } catch { /* ignore */ }

        foreach (var cts in _cancellers.Values)
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        }

        try { _torrents.Dispose(); } catch { /* ignore */ }
    }

    private sealed class SpeedSample
    {
        public SpeedSample(long bytes, DateTime time)
        {
            Bytes = bytes;
            Time = time;
        }

        public long Bytes;
        public DateTime Time;
    }
}
