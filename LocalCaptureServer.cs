using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Tiny HTTP server on 127.0.0.1:8795 that browser extensions talk to.
/// POST /capture    {"url":"...","source":"chrome"}        -> enqueues the URL
/// GET  /heartbeat  ?source=firefox                      -> registers the browser
/// GET  /status                                            -> JSON heartbeats
/// </summary>
public sealed class LocalCaptureServer : IDisposable
{
    public const int DefaultPort = 8795;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonOutOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DownloadManager _manager;
    private HttpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();
    private int _port;

    public int Port => _port;
    public bool IsRunning { get; private set; }
    public IReadOnlyDictionary<string, DateTime> Heartbeats => _heartbeats;

    public event EventHandler<CapturedLink>? LinkCaptured;

    /// <summary>Raised by GET /show: a second instance asks us to show up.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>Raised by POST /quit (JSON): scripted graceful shutdown.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Optional URL exclusion (wildcard patterns). True = reject.</summary>
    public Func<string, bool>? IsExcluded { get; set; }

    public LocalCaptureServer(DownloadManager manager)
    {
        _manager = manager;
    }

    /// <summary>Starts the listener on DefaultPort (or the next free port).</summary>
    public bool TryStart()
    {
        // A fresh HttpListener is required per attempt: when Start() fails, .NET
        // disposes the listener internally, so reusing it throws ObjectDisposedException
        // on the next Prefixes access and the port fallback never happens.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var port = DefaultPort + attempt;
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();

                _listener = listener;
                _port = port;
                IsRunning = true;
                _ = Task.Run(() => LoopAsync(_cts.Token));
                return true;
            }
            catch
            {
                try { listener.Close(); } catch { /* already unusable */ }
            }
        }
        return false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;

        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var source = ctx.Request.QueryString["source"] ?? "browser";

            switch (path)
            {
                case "/capture":
                    await HandleCaptureAsync(ctx, source).ConfigureAwait(false);
                    break;

                case "/formats":
                    await HandleFormatsAsync(ctx, source).ConfigureAwait(false);
                    break;

                case "/capture-file":
                    await HandleCaptureFileAsync(ctx, source).ConfigureAwait(false);
                    break;

                case "/heartbeat":
                    _heartbeats[source] = DateTime.UtcNow;
                    await WriteTextAsync(ctx, 200, "ok").ConfigureAwait(false);
                    break;

                case "/show":
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    await WriteTextAsync(ctx, 200, "shown").ConfigureAwait(false);
                    break;

                case "/quit":
                    if (!IsJsonPost(ctx))
                    {
                        await WriteTextAsync(ctx, 415, "JSON only").ConfigureAwait(false);
                        break;
                    }
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    await WriteTextAsync(ctx, 200, "quitting").ConfigureAwait(false);
                    break;

                case "/status":
                    _heartbeats[source] = DateTime.UtcNow;
                    var json = JsonSerializer.Serialize(
                        _heartbeats.ToDictionary(p => p.Key, p => p.Value));
                    await WriteTextAsync(ctx, 200, json, "application/json").ConfigureAwait(false);
                    break;

                default:
                    await WriteTextAsync(ctx, 404, "not found").ConfigureAwait(false);
                    break;
            }
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandleCaptureFileAsync(HttpListenerContext ctx, string source)
    {
        if (ctx.Request.HttpMethod != "POST")
        {
            await WriteTextAsync(ctx, 405, "POST only").ConfigureAwait(false);
            return;
        }

        // Second-instance file forwarding (e.g. double-clicked .torrent):
        // raw bytes + ?filename=. Size-capped, extension-locked.
        var fileName = HttpDownloader.SanitizeFileName(ctx.Request.QueryString["filename"] ?? string.Empty);
        if (!fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(ctx, 400, "only .torrent files are accepted").ConfigureAwait(false);
            return;
        }
        if (ctx.Request.ContentLength64 > 100L * 1024 * 1024)
        {
            await WriteTextAsync(ctx, 413, "file too large").ConfigureAwait(false);
            return;
        }

        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            await ctx.Request.InputStream.CopyToAsync(memory).ConfigureAwait(false);
            bytes = memory.ToArray();
        }
        if (bytes.Length == 0 || bytes.Length > 100 * 1024 * 1024)
        {
            await WriteTextAsync(ctx, 400, "empty file").ConfigureAwait(false);
            return;
        }

        string saved;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ifredrixDownloadManager", "cache", "torrents", "incoming");
            Directory.CreateDirectory(dir);
            saved = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".torrent");
            await File.WriteAllBytesAsync(saved, bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteTextAsync(ctx, 500, "cannot store file: " + ex.Message).ConfigureAwait(false);
            return;
        }

        string? failure = null;
        try
        {
            await _manager.AddDownloadAsync(saved, DownloadType.Torrent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (failure == null)
        {
            LinkCaptured?.Invoke(this, new CapturedLink(saved, source, false));
            await WriteTextAsync(ctx, 200, "queued").ConfigureAwait(false);
        }
        else
        {
            await WriteTextAsync(ctx, 502, "rejected: " + failure).ConfigureAwait(false);
        }
    }

    /// <summary>POST {"url":...} -> quality/size list (yt-dlp -F) for the panel.</summary>
    private async Task HandleFormatsAsync(HttpListenerContext ctx, string source)
    {
        if (ctx.Request.HttpMethod != "POST")
        {
            await WriteTextAsync(ctx, 405, "POST only").ConfigureAwait(false);
            return;
        }

        if (!IsJsonPost(ctx))
        {
            await WriteTextAsync(ctx, 415, "JSON only").ConfigureAwait(false);
            return;
        }

        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        CapturePayload? payload = null;
        try { payload = JsonSerializer.Deserialize<CapturePayload>(body, JsonOpts); } catch { }

        if (payload == null ||
            (string.IsNullOrWhiteSpace(payload.Url) &&
             (payload.Urls == null || payload.Urls.Count == 0)))
        {
            await WriteTextAsync(ctx, 400, "invalid payload").ConfigureAwait(false);
            return;
        }

        // The panel sends every media URL it saw (newest first): signed
        // URLs die within minutes, so try each in order until one yields
        // a list. The winner travels back as sourceUrl - the panel must
        // capture THAT url, because format ids only mean something there.
        var candidates = new List<string>();
        if (payload.Urls != null)
        {
            foreach (var u in payload.Urls)
            {
                var t = (u ?? string.Empty).Trim();
                if (Uri.TryCreate(t, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                    !candidates.Contains(t, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(t);
                }
            }
        }
        var single = (payload.Url ?? string.Empty).Trim();
        if (Uri.TryCreate(single, UriKind.Absolute, out var suri) &&
            (suri.Scheme == Uri.UriSchemeHttp || suri.Scheme == Uri.UriSchemeHttps) &&
            !candidates.Contains(single, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(single);
        }
        if (candidates.Count == 0)
        {
            await WriteTextAsync(ctx, 400, "invalid payload").ConfigureAwait(false);
            return;
        }

        List<StreamChoice> choices = new();
        var winner = candidates[0];
        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var list = await _manager.ListStreamChoicesAsync(
                        candidate, _cts.Token, payload.Referer, payload.Ua, payload.Cookies)
                    .ConfigureAwait(false);
                if (list.Count > 0)
                {
                    choices = list;
                    winner = candidate;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (choices.Count == 0)
        {
            var detail = lastError != null
                ? FriendlyFormatsError(lastError.Message)
                : "No quality choices found.";
            await WriteTextAsync(ctx, 502, "failed: " + detail).ConfigureAwait(false);
            return;
        }

        _heartbeats[source] = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(
            new { ok = true, formats = choices, sourceUrl = winner }, JsonOutOpts);
        await WriteTextAsync(ctx, 200, json, "application/json").ConfigureAwait(false);
    }

    /// <summary>
    /// Raw yt-dlp dumps embed huge signed URLs and English-only jargon.
    /// DRM is the common dead end (studio-licensed segments no tool may
    /// fetch): say so plainly, in both UI languages, instead of the dump.
    /// </summary>
    private static string FriendlyFormatsError(string message)
    {
        if (message.Contains("DRM protected", StringComparison.OrdinalIgnoreCase))
        {
            return "Video ini terproteksi DRM (lisensi studio) sehingga tidak bisa diunduh tool apapun. / This video is DRM-protected (studio license) and cannot be downloaded by any tool.";
        }
        return message;
    }

    private async Task HandleCaptureAsync(HttpListenerContext ctx, string source)
    {
        if (ctx.Request.HttpMethod != "POST")
        {
            await WriteTextAsync(ctx, 405, "POST only").ConfigureAwait(false);
            return;
        }

        if (!IsJsonPost(ctx))
        {
            await WriteTextAsync(ctx, 415, "JSON only").ConfigureAwait(false);
            return;
        }

        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        CapturePayload? payload = null;
        try { payload = JsonSerializer.Deserialize<CapturePayload>(body, JsonOpts); } catch { }

        if (payload == null || string.IsNullOrWhiteSpace(payload.Url) ||
            !Uri.TryCreate(payload.Url, UriKind.Absolute, out _))
        {
            await WriteTextAsync(ctx, 400, "invalid payload").ConfigureAwait(false);
            return;
        }

        if (IsExcluded?.Invoke(payload.Url) == true)
        {
            await WriteTextAsync(ctx, 403, "excluded by pattern").ConfigureAwait(false);
            return;
        }

        // Same URL *and* format already active -> answer OK idempotently so
        // the browser side cancels its copy instead of downloading a
        // duplicate. A different picked quality is a different task.
        var wantedFormat = (payload.Format ?? string.Empty).Trim();
        var alreadyActive = _manager.GetAllTasks().Any(t =>
            string.Equals(t.Url, payload.Url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.FormatId, wantedFormat, StringComparison.OrdinalIgnoreCase) &&
            t.Status is DownloadStatus.Pending or DownloadStatus.Queued or
                DownloadStatus.Downloading or DownloadStatus.Paused);
        if (alreadyActive)
        {
            _heartbeats[source] = DateTime.UtcNow;
            LinkCaptured?.Invoke(this, new CapturedLink(payload.Url, source, payload.Ui));
            await WriteTextAsync(ctx, 200, "queued").ConfigureAwait(false);
            return;
        }

        _heartbeats[source] = DateTime.UtcNow;

        string? failure = null;
        try
        {
            // yt-dlp must handle: known stream hosts, HLS/DASH playlists,
            // and any URL the panel listed formats for that is not a plain
            // progressive file - format ids only mean something to yt-dlp,
            // so the raw range downloader must never drop them.
            var viaYtDlp =
                StreamCapture.IsStreamingUrl(payload.Url) ||
                StreamCapture.IsPlaylistUrl(payload.Url) ||
                (!string.IsNullOrWhiteSpace(wantedFormat) &&
                 !StreamCapture.IsDirectFileUrl(payload.Url));

            if (viaYtDlp)
            {
                await _manager.AddStreamAsync(
                        payload.Url, wantedFormat, payload.Referer, payload.Ua, payload.Cookies)
                    .ConfigureAwait(false);
            }
            else
            {
                await _manager.AddDownloadAsync(
                        payload.Url, DownloadType.Regular, payload.Referer, payload.Ua, payload.Cookies)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Dead link, duplicate, server refused the probe - tell the caller
            // instead of silently reporting success with no row added.
            failure = ex.Message;
        }

        if (failure == null)
        {
            LinkCaptured?.Invoke(this, new CapturedLink(payload.Url, source, payload.Ui));
            await WriteTextAsync(ctx, 200, "queued").ConfigureAwait(false);
        }
        else
        {
            await WriteTextAsync(ctx, 502, "rejected: " + failure).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// CSRF guard for the JSON APIs: plain web pages can only send
    /// "simple" cross-origin requests (no custom Content-Type without a
    /// preflight this server deliberately does not answer), so any POST
    /// that is not application/json never came from our extension or CLI.
    /// </summary>
    private static bool IsJsonPost(HttpListenerContext ctx) =>
        string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) &&
        (ctx.Request.ContentType ?? string.Empty)
            .StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteTextAsync(HttpListenerContext ctx, int status, string text,
        string contentType = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.LongLength;
        await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        IsRunning = false;
        try { _cts.Cancel(); } catch { }
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _cts.Dispose();
    }

    public sealed class CapturePayload
    {
        // The extensions post lowercase keys; System.Text.Json is case-sensitive by
        // default, so without these every capture would be rejected as invalid.
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "browser";

        // Optional yt-dlp format selector picked in the browser panel
        // (e.g. "399+140"); empty = automatic best.
        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        // Page/iframe URL the media was playing on; forwarded as yt-dlp
        // --referer / HTTP Referer so referer-gated hosts accept the request.
        // Optional: older extensions omit it (= no context, old behaviour).
        [JsonPropertyName("referer")]
        public string Referer { get; set; } = string.Empty;

        // Browser User-Agent + the page's cookies (harvested by the
        // extension): the transfer then runs as the same session that is
        // already playing the video. Both optional.
        [JsonPropertyName("ua")]
        public string Ua { get; set; } = string.Empty;

        [JsonPropertyName("cookies")]
        public List<CookieEntry>? Cookies { get; set; }

        // True when the user picked this row in the in-page panel (as
        // opposed to a silent autodownload/context-menu handoff): the app
        // should pop the progress window, not just a toast. Optional:
        // older extensions omit it (= toast only, old behaviour).
        [JsonPropertyName("ui")]
        public bool Ui { get; set; }

        // Every media URL the panel saw, newest first (signed URLs die
        // within minutes - the server tries each until one lists).
        // Optional: older extensions send only "url".
        [JsonPropertyName("urls")]
        public List<string>? Urls { get; set; }
    }

    public sealed class CapturedLink
    {
        public CapturedLink(string url, string source, bool ui)
        {
            Url = url; Source = source; Ui = ui;
        }
        public string Url { get; }
        public string Source { get; }
        public bool Ui { get; }
    }
}