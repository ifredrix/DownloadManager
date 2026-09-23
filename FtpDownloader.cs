using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

public sealed class FtpProbeResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public long TotalSize { get; set; }
    public bool SupportsRange { get; set; }
    public string? SuggestedFileName { get; set; }
}

/// <summary>
/// Minimal FTP client (RFC 959, passive mode, binary) with multi-connection
/// segmented downloads and resume. Credentials come from the URL userinfo
/// (<c>ftp://user:pass@host/file</c>), anonymous otherwise. Control and data
/// connections can both be routed through Tor.
/// </summary>
public sealed class FtpDownloader
{
    private const int BufferSize = 64 * 1024;

    // FTP servers usually cap connections per IP; stay polite.
    private const int MaxConnections = 8;
    private const long MultiConnectionThreshold = 512 * 1024;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;

    private static readonly Regex PasvRegex =
        new(@"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)", RegexOptions.Compiled);

    private readonly RateLimiter _limiter;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<FtpChunk>> _transfers = new();

    public FtpDownloader(RateLimiter limiter)
    {
        _limiter = limiter;
    }

    public static bool IsFtpUrl(string url)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == "ftps");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSecure(Uri uri) =>
        string.Equals(uri.Scheme, "ftps", StringComparison.OrdinalIgnoreCase);

    private static void ParseAuthority(Uri uri, out string user, out string pass)
    {
        user = "anonymous";
        pass = "ifredrix";
        try
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                if (!string.IsNullOrWhiteSpace(parts[0]))
                {
                    user = Uri.UnescapeDataString(parts[0]);
                }
                if (parts.Length > 1)
                {
                    pass = Uri.UnescapeDataString(parts[1]);
                }
            }
            else if (NetConfig.SiteLogins.TryGetValue(uri.Host, out var saved) &&
                     !string.IsNullOrEmpty(saved.User))
            {
                user = saved.User;
                pass = saved.Pass;
            }
        }
        catch
        {
            // Keep anonymous defaults.
        }
    }

    private static string RemotePath(Uri uri)
    {
        try
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            return string.IsNullOrEmpty(path) ? "/" : path;
        }
        catch
        {
            return uri.AbsolutePath;
        }
    }

    public static string DeriveFileName(string url)
    {
        try
        {
            var name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
            if (!string.IsNullOrWhiteSpace(name)) return HttpDownloader.SanitizeFileName(name);
        }
        catch
        {
            // Fall through.
        }
        return "download-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    public async Task<FtpProbeResult> ProbeAsync(string url, bool useTor = false, CancellationToken ct = default)
    {
        var result = new FtpProbeResult();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsFtpUrl(url))
        {
            result.Error = "Not an FTP link.";
            return result;
        }

        try
        {
            ParseAuthority(uri, out var user, out var pass);
            using var session = await FtpSession.OpenAsync(
                uri.Host, uri.Port == -1 ? 21 : uri.Port, user, pass, useTor, IsSecure(uri), ct).ConfigureAwait(false);

            var size = await session.GetSizeAsync(RemotePath(uri), ct).ConfigureAwait(false);
            if (size < 0)
            {
                result.Error = "Server did not report a file size.";
                return result;
            }

            result.TotalSize = size;
            result.SupportsRange = await session.SupportsResumeAsync(ct).ConfigureAwait(false);
            result.SuggestedFileName = DeriveFileName(url);
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
        if (!Uri.TryCreate(task.Url, UriKind.Absolute, out var uri) || !IsFtpUrl(task.Url))
        {
            throw new ArgumentException("Not an FTP link.", nameof(task));
        }

        var destination = task.SavePath;
        var total = task.FileSize;
        var sidecar = destination + ".tdpart";

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (task.UseTor && !await TorProxy.IsAvailableAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Tor is not reachable at {TorProxy.Host}:{TorProxy.Port}. " +
                "Start Tor Browser (uses 9150) or the Tor expert bundle (uses 9050).");
        }

        if (!task.SupportsRange || total <= 0)
        {
            TryDelete(sidecar);
            TryDelete(destination);
            task.DownloadedBytes = 0;
            await DownloadRangeAsync(task, 0, long.MaxValue, ct).ConfigureAwait(false);
            return;
        }

        using (var reserve = new FileStream(destination, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        {
            if (reserve.Length != total) reserve.SetLength(total);
        }

        var chunks = LoadChunks(sidecar, task.Url, total)
                     ?? BuildChunks(total, task.UseTor
                         ? Math.Clamp(connections, 1, 2)
                         : Math.Clamp(connections, 1, MaxConnections));
        _transfers[task.Id] = chunks;
        task.DownloadedBytes = chunks.Sum(c => c.Position);

        try
        {
            using var saveTimer = new System.Threading.Timer(
                _ => SaveChunks(sidecar, task.Url, total, chunks), null, 2000, 2000);
            await Task.WhenAll(chunks.Select(c => DownloadChunkAsync(task, c, ct))).ConfigureAwait(false);
        }
        finally
        {
            SaveChunks(sidecar, task.Url, total, chunks);
        }

        if (!ct.IsCancellationRequested) TryDelete(sidecar);
    }

    private async Task DownloadChunkAsync(DownloadTask task, FtpChunk chunk, CancellationToken ct)
    {
        var offset = chunk.Position;
        chunk.Active = true;
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    offset += await DownloadRangeAsync(task, chunk.Start + offset, chunk.End, ct)
                        .ConfigureAwait(false);
                    chunk.Position = offset;
                    return;
                }
                catch (OperationCanceledException)
                {
                    chunk.Position = offset;
                    throw;
                }
                catch (Exception ex) when (IsRetriable(ex) && attempt < MaxAttempts)
                {
                    chunk.Position = offset;
                    await Task.Delay(500 * attempt, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            chunk.Active = false;
        }
    }

    /// <summary>Live chunk snapshot for the progress window.</summary>
    public List<SegmentStat> GetSegments(string taskId)
    {
        var empty = new List<SegmentStat>();
        if (!_transfers.TryGetValue(taskId, out var chunks)) return empty;

        var stats = new List<SegmentStat>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var length = c.End - c.Start + 1;
            var done = Math.Min(c.Position, length);
            stats.Add(new SegmentStat
            {
                Index = i,
                Start = c.Start,
                Length = length,
                Downloaded = done,
                Active = c.Active && done < length
            });
        }
        return stats;
    }

    public void DropPlan(string taskId) => _transfers.TryRemove(taskId, out _);

    private static bool IsRetriable(Exception ex) =>
        ex is TimeoutException || ex is IOException || ex is InvalidOperationException;

    /// <summary>Downloads [from, to] at the matching file offset. Returns bytes written.</summary>
    private async Task<long> DownloadRangeAsync(DownloadTask task, long from, long to, CancellationToken ct)
    {
        if (!Uri.TryCreate(task.Url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Not an FTP link.", nameof(task));
        }

        ParseAuthority(uri, out var user, out var pass);
        using var session = await FtpSession.OpenAsync(
            uri.Host, uri.Port == -1 ? 21 : uri.Port, user, pass, task.UseTor, IsSecure(uri), ct).ConfigureAwait(false);

        var written = 0L;
        await session.RetrieveAsync(
            RemotePath(uri), from, to, IsSecure(uri),
            async (buffer, read) =>
            {
                using var file = new FileStream(task.SavePath, FileMode.OpenOrCreate,
                    FileAccess.Write, FileShare.ReadWrite);
                file.Seek(from + written, SeekOrigin.Begin);
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;
                task.AddDownloadedBytes(read);
                await _limiter.ThrottleAsync(read, ct).ConfigureAwait(false);
                await TaskLimits.ThrottleAsync(task.Id, task.SpeedLimitBps, read, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

        return written;
    }

    private static List<FtpChunk> BuildChunks(long total, int connections)
    {
        if (total < MultiConnectionThreshold) connections = 1;
        connections = Math.Clamp(connections, 1, MaxConnections);

        var chunks = new List<FtpChunk>();
        var part = total / connections;
        long cursor = 0;
        for (var i = 0; i < connections; i++)
        {
            var end = i == connections - 1 ? total - 1 : cursor + part - 1;
            chunks.Add(new FtpChunk { Start = cursor, End = end, Position = 0 });
            cursor = end + 1;
        }
        return chunks;
    }

    private static List<FtpChunk>? LoadChunks(string sidecar, string url, long total)
    {
        try
        {
            if (!File.Exists(sidecar)) return null;
            var state = JsonSerializer.Deserialize<FtpResumeState>(File.ReadAllText(sidecar));
            if (state == null || state.Kind != "ftp") return null;
            if (!string.Equals(state.Url, url, StringComparison.Ordinal)) return null;
            if (state.Total != total || state.Chunks is not { Count: > 0 }) return null;

            var cleaned = state.Chunks
                .Where(c => c.End >= c.Start && c.Position >= 0 && c.Position <= c.End - c.Start + 1)
                .ToList();
            return cleaned.Count == state.Chunks.Count ? cleaned : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveChunks(string sidecar, string url, long total, List<FtpChunk> chunks)
    {
        if (chunks.Count == 0) return;
        try
        {
            File.WriteAllText(sidecar, JsonSerializer.Serialize(new FtpResumeState
            {
                Kind = "ftp",
                Url = url,
                Total = total,
                Chunks = chunks.Select(c => new FtpChunk
                {
                    Start = c.Start,
                    End = c.End,
                    Position = c.Position
                }).ToList()
            }));
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class FtpResumeState
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("chunks")] public List<FtpChunk> Chunks { get; set; } = new();
    }

    private sealed class FtpChunk
    {
        [JsonPropertyName("start")] public long Start { get; set; }
        [JsonPropertyName("end")] public long End { get; set; }
        [JsonPropertyName("pos")] public long Position { get; set; }
        [JsonIgnore] public bool Active { get; set; }
    }

    /// <summary>One FTP control connection (plus short-lived data connections).</summary>
    private sealed class FtpSession : IDisposable
    {
        private readonly TcpClient? _owned;
        private readonly Stream _control;
        private StreamReader _reader;
        private StreamWriter _writer;
        private SslStream? _tls;
        private bool _disposed;

        private FtpSession(TcpClient? owned, Stream control)
        {
            _owned = owned;
            _control = control;
            _reader = new StreamReader(control, Encoding.ASCII, false, 1024, leaveOpen: true);
            _writer = new StreamWriter(control, Encoding.ASCII, 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
        }

        public static async Task<FtpSession> OpenAsync(
            string host, int port, string user, string pass, bool useTor, bool secure, CancellationToken ct)
        {
            TcpClient? owned = null;
            Stream control;
            if (useTor)
            {
                control = await TorProxy.ConnectAsync(host, port, ct).ConfigureAwait(false);
            }
            else
            {
                owned = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ConnectTimeout);
                try
                {
                    await owned.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    owned.Close();
                    throw new TimeoutException($"Timed out connecting to {host}:{port}.");
                }
                control = owned.GetStream();
            }

            var session = new FtpSession(owned, control);
            try
            {
                var greeting = await session.ReadReplyAsync(ct).ConfigureAwait(false);
                if (greeting.Code != 220) throw new InvalidOperationException("Bad FTP greeting: " + greeting.Text);

                if (secure)
                {
                    // Explicit FTPS (FTPES): upgrade before any credential goes out.
                    var auth = await session.CommandAsync("AUTH TLS", ct).ConfigureAwait(false);
                    if (auth.Code != 234) throw new InvalidOperationException("Server refused FTPS: " + auth.Text);
                    await session.UpgradeToTlsAsync(host, ct).ConfigureAwait(false);
                }

                var u = await session.CommandAsync("USER " + user, ct).ConfigureAwait(false);
                if (u.Code == 331)
                {
                    var p = await session.CommandAsync("PASS " + pass, ct).ConfigureAwait(false);
                    if (p.Code != 230) throw new InvalidOperationException("FTP login failed: " + p.Text);
                }
                else if (u.Code != 230)
                {
                    throw new InvalidOperationException("FTP login failed: " + u.Text);
                }

                var t = await session.CommandAsync("TYPE I", ct).ConfigureAwait(false);
                if (t.Code != 200) throw new InvalidOperationException("FTP binary mode refused: " + t.Text);

                if (secure)
                {
                    var pbsz = await session.CommandAsync("PBSZ 0", ct).ConfigureAwait(false);
                    if (pbsz.Code != 200) throw new InvalidOperationException("FTPS protection refused: " + pbsz.Text);
                    var prot = await session.CommandAsync("PROT P", ct).ConfigureAwait(false);
                    if (prot.Code != 200) throw new InvalidOperationException("FTPS protection refused: " + prot.Text);
                }

                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private async Task UpgradeToTlsAsync(string host, CancellationToken ct)
        {
            var tls = new SslStream(_control, leaveInnerStreamOpen: false);
            try
            {
                await tls.AuthenticateAsClientAsync(host, null, System.Security.Authentication.SslProtocols.None, false)
                    .ConfigureAwait(false);
            }
            catch
            {
                try { await tls.DisposeAsync().ConfigureAwait(false); } catch { }
                throw new InvalidOperationException("FTPS handshake failed (untrusted certificate?).");
            }
            try { _writer.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            _tls = tls;
            _reader = new StreamReader(tls, Encoding.ASCII, false, 1024, leaveOpen: true);
            _writer = new StreamWriter(tls, Encoding.ASCII, 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
        }

        public async Task<long> GetSizeAsync(string path, CancellationToken ct)
        {
            var reply = await CommandAsync("SIZE " + path, ct).ConfigureAwait(false);
            if (reply.Code != 213) return -1;
            return long.TryParse(reply.Text.Trim(), out var size) ? size : -1;
        }

        public async Task<bool> SupportsResumeAsync(CancellationToken ct)
        {
            var reply = await CommandAsync("REST 0", ct).ConfigureAwait(false);
            return reply.Code == 350;
        }

        public async Task RetrieveAsync(
            string path, long from, long to, bool secure,
            Func<byte[], int, Task> onData, CancellationToken ct)
        {
            var pasv = await CommandAsync("PASV", ct).ConfigureAwait(false);
            if (pasv.Code != 227) throw new InvalidOperationException("FTP passive mode refused: " + pasv.Text);

            var match = PasvRegex.Match(pasv.Text);
            if (!match.Success) throw new InvalidOperationException("Bad PASV reply: " + pasv.Text);
            var nums = match.Groups.Values.Skip(1).Select(g => int.Parse(g.Value)).ToArray();
            var dataHost = string.Join(".", nums.Take(4));
            var dataPort = nums[4] * 256 + nums[5];

            TcpClient? dataOwned = null;
            Stream data;
            SslStream? dataTls = null;
            if (IsTorSession)
            {
                data = await TorProxy.ConnectAsync(dataHost, dataPort, ct).ConfigureAwait(false);
            }
            else
            {
                dataOwned = new TcpClient();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ConnectTimeout);
                    try
                    {
                        await dataOwned.ConnectAsync(dataHost, dataPort, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException("Timed out opening the FTP data connection.");
                    }
                    data = dataOwned.GetStream();
                }
                catch
                {
                    dataOwned.Close();
                    throw;
                }
            }

            // NOTE: no TLS session reuse on the data connection (SslStream does
            // not expose session handles). Servers that mandate reuse (strict
            // vsftpd) will reject FTPS transfers; most others accept this.
            if (secure && !IsTorSession)
            {
                try
                {
                    dataTls = new SslStream(data, leaveInnerStreamOpen: false);
                    await dataTls.AuthenticateAsClientAsync(dataHost, null,
                        System.Security.Authentication.SslProtocols.None, false).ConfigureAwait(false);
                    data = dataTls;
                }
                catch
                {
                    try { dataTls?.Dispose(); } catch { }
                    try { await data.DisposeAsync().ConfigureAwait(false); } catch { }
                    try { dataOwned?.Close(); } catch { }
                    throw new InvalidOperationException("FTPS data handshake failed.");
                }
            }

            try
            {
                if (from > 0)
                {
                    var rest = await CommandAsync("REST " + from, ct).ConfigureAwait(false);
                    if (rest.Code != 350)
                    {
                        throw new InvalidOperationException("Server refused resume (REST rejected).");
                    }
                }

                var retr = await CommandAsync("RETR " + path, ct).ConfigureAwait(false);
                if (retr.Code != 150 && retr.Code != 125)
                {
                    throw new InvalidOperationException("FTP retrieve failed: " + retr.Text);
                }

                var buffer = new byte[BufferSize];
                var lastProgress = Environment.TickCount64;
                int read;
                while ((read = await data.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Environment.TickCount64 - lastProgress > (long)StallTimeout.TotalMilliseconds)
                    {
                        throw new TimeoutException("FTP data stalled: no data for 30 seconds.");
                    }
                    lastProgress = Environment.TickCount64;
                    await onData(buffer, read).ConfigureAwait(false);
                }

                var done = await ReadReplyAsync(ct).ConfigureAwait(false);
                if (done.Code != 226 && done.Code != 250)
                {
                    throw new InvalidOperationException("FTP transfer failed: " + done.Text);
                }
            }
            finally
            {
                try { await data.DisposeAsync().ConfigureAwait(false); } catch { }
                try { dataOwned?.Close(); } catch { }
            }
        }

        private bool IsTorSession => _owned == null;

        private async Task<(int Code, string Text)> CommandAsync(string command, CancellationToken ct)
        {
            await _writer.WriteLineAsync(command.AsMemory(), ct).ConfigureAwait(false);
            return await ReadReplyAsync(ct).ConfigureAwait(false);
        }

        private async Task<(int Code, string Text)> ReadReplyAsync(CancellationToken ct)
        {
            var first = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                        ?? throw new IOException("FTP server closed the connection.");
            if (first.Length < 3 || !int.TryParse(first.AsSpan(0, 3), out var code))
            {
                throw new IOException("Bad FTP reply: " + first);
            }
            if (first.Length > 3 && first[3] == '-')
            {
                string? line;
                do
                {
                    line = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                           ?? throw new IOException("FTP server closed the connection.");
                } while (!(line.Length >= 4 && line.StartsWith(first.AsSpan(0, 3).ToString()) && line[3] == ' '));
                return (code, line.Length > 4 ? line[4..] : string.Empty);
            }
            return (code, first.Length > 4 ? first[4..] : string.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _tls?.Dispose(); } catch { }
            try { _control.Dispose(); } catch { }
            try { _owned?.Close(); } catch { }
        }
    }
}
