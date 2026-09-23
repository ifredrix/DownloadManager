using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

public sealed class ProbeResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public long TotalSize { get; set; }
    public bool SupportsRange { get; set; }
    public string? SuggestedFileName { get; set; }
}

/// <summary>
/// Multi-connection HTTP(S) downloader with <b>dynamic segmentation</b>:
/// the file is split into ~1 MB segments queued centrally, so fast
/// connections steal work from slow ones instead of idling on fixed chunks.
/// Progress is mirrored to a ".tdpart" sidecar for resume across restarts.
/// HTTP/2 is preferred (multiplexed streams over reused connections) with
/// automatic fallback to HTTP/1.1.
/// </summary>
public sealed class HttpDownloader
{
    private const int BufferSize = 64 * 1024;

    // Hard ceiling for one download. 25 is the sweet spot on most servers;
    // 32 leaves headroom without tripping per-IP connection limits.
    private const int MaxConnections = 32;
    private const long MultiConnectionThreshold = 512 * 1024;

    // Dynamic-segment sizing: small enough that a stalled segment never
    // blocks the whole file, big enough to avoid request overhead.
    private const long SegmentSize = 1024 * 1024;
    private const int MaxSegments = 20000;

    // A segment with no progress for this long is abandoned and requeued.
    private static readonly TimeSpan SegmentStallTimeout = TimeSpan.FromSeconds(30);
    private const int SegmentMaxAttempts = 5;

    private HttpClient _client;
    private HttpClient _transfer;
    private readonly RateLimiter _limiter;

    private static SocketsHttpHandler BuildHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 256,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            // Let many concurrent range streams multiplex instead of queuing
            // behind a single HTTP/2 connection.
            EnableMultipleHttp2Connections = true,
            UseCookies = true,
            CookieContainer = NetConfig.Cookies
        };
        NetConfig.ApplyProxy(handler);
        return handler;
    }

    private static void Tune(HttpClient client, TimeSpan timeout)
    {
        client.Timeout = timeout;
        // Prefer HTTP/2 where the server speaks it; fall back to 1.1.
        client.DefaultRequestVersion = new Version(2, 0);
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public HttpDownloader(RateLimiter limiter)
    {
        _limiter = limiter;

        _client = new HttpClient(BuildHandler(), disposeHandler: true);
        Tune(_client, TimeSpan.FromSeconds(60));

        // Transfers use their own client with no global timeout: a slow
        // segment is handled by the per-segment stall watchdog instead.
        _transfer = new HttpClient(BuildHandler(), disposeHandler: true);
        Tune(_transfer, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Rebuilds transfer clients after proxy/connection changes.</summary>
    public void ReloadConnection()
    {
        try { _client.Dispose(); } catch { }
        try { _transfer.Dispose(); } catch { }
        try { _torClient?.Dispose(); } catch { }
        _torClient = null;
        _torEndpoint = string.Empty;

        _client = new HttpClient(BuildHandler(), disposeHandler: true);
        Tune(_client, TimeSpan.FromSeconds(60));
        _transfer = new HttpClient(BuildHandler(), disposeHandler: true);
        Tune(_transfer, Timeout.InfiniteTimeSpan);
    }

    private static void ApplyAuth(HttpRequestMessage request, string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            var basic = NetConfig.BasicAuthFor(uri);
            if (basic != null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", basic);
            }
        }
        catch
        {
            // Auth is best effort.
        }
    }

    private HttpClient? _torClient;
    private string _torEndpoint = string.Empty;
    private readonly ConcurrentDictionary<string, SegmentPlan> _plans = new();

    /// <summary>
    /// SOCKS5-routed client for Tor downloads (.onion or forced). Rebuilt
    /// when the configured endpoint changes. Hostnames travel unresolved
    /// so Tor does the resolving (no local DNS leak).
    /// </summary>
    private HttpClient TorClient()
    {
        var endpoint = TorProxy.Host + ":" + TorProxy.Port;
        if (_torClient == null || _torEndpoint != endpoint)
        {
            try { _torClient?.Dispose(); } catch { }

            var host = TorProxy.Host;
            var port = TorProxy.Port;
            var handler = BuildHandler();
            handler.ConnectCallback = (ctx, ct) =>
            {
                var dest = ctx.DnsEndPoint;
                return new ValueTask<Stream>(TorProxy.ConnectAsync(host, port, dest.Host, dest.Port, ct));
            };

            _torClient = new HttpClient(handler, disposeHandler: true);
            Tune(_torClient, Timeout.InfiniteTimeSpan);
            _torEndpoint = endpoint;
        }
        return _torClient;
    }

    private static async Task EnsureTorAsync()
    {
        if (await TorProxy.IsAvailableAsync().ConfigureAwait(false)) return;
        throw new InvalidOperationException(
            $"Tor is not reachable at {TorProxy.Host}:{TorProxy.Port}. " +
            "Start Tor Browser (uses 9150) or the Tor expert bundle (uses 9050).");
    }

    /// <summary>Asks the server for size, filename and range support.</summary>
    public async Task<ProbeResult> ProbeAsync(string url, bool useTor = false, CancellationToken ct = default)
    {
        var result = new ProbeResult();
        try
        {
            var viaTor = useTor || TorProxy.IsOnionUrl(url);
            HttpClient client;
            if (viaTor)
            {
                await EnsureTorAsync().ConfigureAwait(false);
                client = TorClient();
            }
            else
            {
                client = _client;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            ApplyAuth(request, url);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                return result;
            }

            result.SuggestedFileName = TryGetFileName(response, url);

            if (response.StatusCode == HttpStatusCode.PartialContent &&
                response.Content.Headers.ContentRange?.Length is { } total)
            {
                result.TotalSize = total;
                result.SupportsRange = true;
            }

            if (!result.SupportsRange && response.Content.Headers.ContentLength is { } len)
            {
                result.TotalSize = len;
            }

            if (response.Headers.AcceptRanges.Contains("bytes") && result.TotalSize > 0)
            {
                result.SupportsRange = true;
            }

            result.Ok = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task DownloadAsync(DownloadTask task, int connections, CancellationToken ct)
    {
        var destination = task.SavePath;
        var total = task.FileSize;
        var sidecar = destination + ".tdpart";

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (task.UseTor || TorProxy.IsOnionUrl(task.Url))
        {
            task.UseTor = true;
            await EnsureTorAsync().ConfigureAwait(false);
        }

        if (!task.SupportsRange || total <= 0)
        {
            // No range support (or unknown size): resume is impossible,
            // so restart cleanly instead of corrupting the file.
            TryDelete(sidecar);
            TryDelete(destination);
            task.DownloadedBytes = 0;
            await DownloadRangeAsync(task, 0, long.MaxValue, 0, null, ct).ConfigureAwait(false);
            return;
        }

        using (var reserve = new FileStream(destination, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        {
            if (reserve.Length != total)
            {
                reserve.SetLength(total);
            }
        }

        var plan = SegmentPlan.LoadOrCreate(sidecar, task.Url, total);
        _plans[task.Id] = plan;
        task.DownloadedBytes = plan.DoneBytes;

        try
        {
            using var saveTimer = new System.Threading.Timer(
                _ => plan.Save(sidecar), null, 2000, 2000);

            // Small files are not worth many handshakes.
            var workers = total < MultiConnectionThreshold
                ? 1
                : Math.Clamp(connections, 1, MaxConnections);
            workers = Math.Min(workers, Math.Max(1, plan.PendingCount));
            if (task.UseTor)
            {
                // Tor guard nodes choke on many parallel streams; fewer,
                // longer-lived connections aggregate better.
                workers = Math.Min(workers, 4);
            }

            var runs = Enumerable.Range(0, workers)
                .Select(id => SegmentWorkerAsync(task, plan, id, ct));
            await Task.WhenAll(runs).ConfigureAwait(false);
        }
        finally
        {
            plan.Save(sidecar);
        }

        if (!ct.IsCancellationRequested)
        {
            TryDelete(sidecar);
        }
    }

    private async Task SegmentWorkerAsync(DownloadTask task, SegmentPlan plan, int workerId, CancellationToken ct)
    {
        var state = plan.Workers.GetOrAdd(workerId, _ => new SegmentPlan.WorkerState());
        while (plan.TryTake(out var segment))
        {
            ct.ThrowIfCancellationRequested();
            state.CurrentSegment = segment;
            state.SegmentBytes = 0;
            await DownloadSegmentAsync(task, plan, segment,
                n => { state.SegmentBytes += n; state.TotalBytes += n; }, ct).ConfigureAwait(false);
            state.CurrentSegment = -1;
            plan.MarkDone(segment);
        }
    }

    private async Task DownloadSegmentAsync(
        DownloadTask task, SegmentPlan plan, int segment, Action<long> onBytes, CancellationToken ct)
    {
        var start = segment * plan.SegmentSize;
        var end = Math.Min(start + plan.SegmentSize - 1, plan.Total - 1);
        var offset = 0L;

        for (var attempt = 1; attempt <= SegmentMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                offset += await DownloadRangeAsync(task, start + offset, end, start + offset, onBytes, ct)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetriable(ex) && attempt < SegmentMaxAttempts)
            {
                // Stalled or dropped connection: back off and re-request
                // only the missing tail of this segment.
                await Task.Delay(500 * attempt, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetriable(Exception ex) =>
        ex is TimeoutException || ex is IOException || ex is HttpRequestException;

    /// <summary>
    /// Downloads [from, to] and appends it at absolute file <paramref name="fileOffset"/>.
    /// Returns the bytes written. Throws <see cref="TimeoutException"/> on stall.
    /// </summary>
    private async Task<long> DownloadRangeAsync(
        DownloadTask task, long from, long to, long fileOffset,
        Action<long>? onBytes = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
        request.Headers.Range = to == long.MaxValue
            ? new RangeHeaderValue(from, null)
            : new RangeHeaderValue(from, to);
        ApplyAuth(request, task.Url);

        var client = task.UseTor ? TorClient() : _transfer;
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            throw new InvalidOperationException("The server rejected the range request (HTTP 416).");
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        using var file = new FileStream(task.SavePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        file.Seek(fileOffset, SeekOrigin.Begin);

        var buffer = new byte[BufferSize];
        var written = 0L;
        var lastProgress = Environment.TickCount64;

        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (Environment.TickCount64 - lastProgress > (long)SegmentStallTimeout.TotalMilliseconds)
            {
                throw new TimeoutException("Segment stalled: no data for 30 seconds.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;
            lastProgress = Environment.TickCount64;
            task.AddDownloadedBytes(read);
            await _limiter.ThrottleAsync(read, ct).ConfigureAwait(false);
            await TaskLimits.ThrottleAsync(task.Id, task.SpeedLimitBps, read, ct).ConfigureAwait(false);
            try { onBytes?.Invoke(read); } catch { /* telemetry only */ }
        }

        await file.FlushAsync(ct).ConfigureAwait(false);
        return written;
    }

    private static void TryDelete(string path)
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

    private static string? TryGetFileName(HttpResponseMessage response, string url)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        if (disposition != null)
        {
            var candidate = disposition.FileNameStar ?? disposition.FileName;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return SanitizeFileName(candidate.Trim('"', '\'', ' '));
            }
        }

        try
        {
            var uri = new Uri(url);
            var name = Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));
            if (!string.IsNullOrWhiteSpace(name)) return SanitizeFileName(name);
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "download";

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }

    /// <summary>Live segment snapshot (done/active/pending) for the progress window.</summary>
    public List<SegmentStat> GetSegments(string taskId)
    {
        var empty = new List<SegmentStat>();
        if (!_plans.TryGetValue(taskId, out var plan)) return empty;

        HashSet<int> done = plan.DoneSnapshot();

        var active = new Dictionary<int, long>();
        foreach (var pair in plan.Workers)
        {
            var seg = pair.Value.CurrentSegment;
            if (seg >= 0) active[seg] = pair.Value.SegmentBytes;
        }

        var stats = new List<SegmentStat>(plan.SegmentCount);
        for (var i = 0; i < plan.SegmentCount; i++)
        {
            var length = plan.SegmentLength(i);
            var isDone = done.Contains(i);
            active.TryGetValue(i, out var live);
            stats.Add(new SegmentStat
            {
                Index = i,
                Start = i * plan.SegmentSize,
                Length = length,
                Downloaded = isDone ? length : Math.Min(live, length),
                Active = !isDone && active.ContainsKey(i)
            });
        }
        return stats;
    }

    public void DropPlan(string taskId) => _plans.TryRemove(taskId, out _);

    /// <summary>
    /// Central segment queue: every worker takes the next unfinished segment,
    /// so throughput follows the fastest connections (dynamic segmentation).
    /// </summary>
    private sealed class SegmentPlan
    {
        public sealed class WorkerState
        {
            public int CurrentSegment = -1;
            public long SegmentBytes;
            public long TotalBytes;
        }

        private readonly ConcurrentQueue<int> _pending = new();
        private readonly HashSet<int> _done = new();
        private readonly object _gate = new();

        public readonly ConcurrentDictionary<int, WorkerState> Workers = new();

        public string Url { get; }
        public long Total { get; }
        public long SegmentSize { get; }
        public int SegmentCount { get; }

        public int PendingCount => _pending.Count;

        public long DoneBytes
        {
            get
            {
                lock (_gate)
                {
                    var bytes = 0L;
                    foreach (var seg in _done) bytes += SegmentLength(seg);
                    return bytes;
                }
            }
        }

        private SegmentPlan(string url, long total, long segSize)
        {
            Url = url;
            Total = total;
            SegmentSize = segSize;
            SegmentCount = (int)((total + segSize - 1) / segSize);
        }

        public long SegmentLength(int segment)
        {
            var start = segment * SegmentSize;
            return Math.Min(start + SegmentSize, Total) - start;
        }

        public bool TryTake(out int segment) => _pending.TryDequeue(out segment);

        public void MarkDone(int segment)
        {
            lock (_gate) { _done.Add(segment); }
        }

        public HashSet<int> DoneSnapshot()
        {
            lock (_gate) { return new HashSet<int>(_done); }
        }

        public void Save(string sidecar)
        {
            try
            {
                List<int> done;
                lock (_gate) { done = _done.ToList(); }
                var state = new ResumeStateV2
                {
                    Url = Url,
                    Total = Total,
                    SegmentSize = SegmentSize,
                    Done = done
                };
                File.WriteAllText(sidecar, JsonSerializer.Serialize(state));
            }
            catch
            {
                // Sidecar persistence is best-effort only.
            }
        }

        public static SegmentPlan LoadOrCreate(string sidecar, string url, long total)
        {
            var segSize = HttpDownloader.SegmentSize;
            if (total / segSize > MaxSegments)
            {
                segSize = (total + MaxSegments - 1) / MaxSegments;
            }

            var plan = new SegmentPlan(url, total, segSize);

            var done = LoadDone(sidecar, url, total, segSize, plan.SegmentCount);
            foreach (var seg in done) plan._done.Add(seg);
            for (var i = 0; i < plan.SegmentCount; i++)
            {
                if (!plan._done.Contains(i)) plan._pending.Enqueue(i);
            }

            return plan;
        }

        private static HashSet<int> LoadDone(
            string sidecar, string url, long total, long segSize, int segCount)
        {
            var done = new HashSet<int>();
            try
            {
                if (!File.Exists(sidecar)) return done;

                var json = File.ReadAllText(sidecar);

                // Current format.
                try
                {
                    var v2 = JsonSerializer.Deserialize<ResumeStateV2>(json);
                    if (v2 != null && v2.Total == total &&
                        string.Equals(v2.Url, url, StringComparison.Ordinal) &&
                        v2.SegmentSize == segSize && v2.Done != null)
                    {
                        foreach (var seg in v2.Done)
                        {
                            if (seg >= 0 && seg < segCount) done.Add(seg);
                        }
                        return done;
                    }
                }
                catch
                {
                    // Fall through to the legacy format below.
                }

                // Legacy fixed-chunk format: any segment fully covered by
                // already-written chunk ranges counts as done.
                var legacy = JsonSerializer.Deserialize<LegacyResumeState>(json);
                if (legacy?.Chunks is { Count: > 0 } chunks && legacy.Total == total)
                {
                    foreach (var seg in Enumerable.Range(0, segCount))
                    {
                        var segStart = seg * segSize;
                        var segEnd = Math.Min(segStart + segSize, total) - 1;
                        foreach (var c in legacy.Chunks)
                        {
                            if (c.Start <= segStart && c.Start + c.Position - 1 >= segEnd)
                            {
                                done.Add(seg);
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Corrupt sidecar: start over.
                done.Clear();
            }

            return done;
        }
    }

    private sealed class ResumeStateV2
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 2;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("seg")] public long SegmentSize { get; set; }
        [JsonPropertyName("done")] public List<int> Done { get; set; } = new();
    }

    // Previous fixed-chunk sidecar shape, read-only for migration.
    private sealed class LegacyResumeState
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("chunks")] public List<LegacyChunk> Chunks { get; set; } = new();
    }

    private sealed class LegacyChunk
    {
        [JsonPropertyName("start")] public long Start { get; set; }
        [JsonPropertyName("end")] public long End { get; set; }
        [JsonPropertyName("pos")] public long Position { get; set; }
    }
}
