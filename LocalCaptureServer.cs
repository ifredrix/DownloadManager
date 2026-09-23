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

    private readonly DownloadManager _manager;
    private HttpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();
    private int _port;

    public int Port => _port;
    public bool IsRunning { get; private set; }
    public IReadOnlyDictionary<string, DateTime> Heartbeats => _heartbeats;

    public event EventHandler<CapturedLink>? LinkCaptured;

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

                case "/capture-file":
                    await HandleCaptureFileAsync(ctx, source).ConfigureAwait(false);
                    break;

                case "/heartbeat":
                    _heartbeats[source] = DateTime.UtcNow;
                    await WriteTextAsync(ctx, 200, "ok").ConfigureAwait(false);
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
            LinkCaptured?.Invoke(this, new CapturedLink(saved, source));
            await WriteTextAsync(ctx, 200, "queued").ConfigureAwait(false);
        }
        else
        {
            await WriteTextAsync(ctx, 502, "rejected: " + failure).ConfigureAwait(false);
        }
    }

    private async Task HandleCaptureAsync(HttpListenerContext ctx, string source)
    {
        if (ctx.Request.HttpMethod != "POST")
        {
            await WriteTextAsync(ctx, 405, "POST only").ConfigureAwait(false);
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

        _heartbeats[source] = DateTime.UtcNow;

        string? failure = null;
        try
        {
            if (StreamCapture.IsStreamingUrl(payload.Url) || StreamCapture.IsPlaylistUrl(payload.Url))
            {
                await _manager.AddStreamAsync(payload.Url).ConfigureAwait(false);
            }
            else
            {
                await _manager.AddDownloadAsync(payload.Url, DownloadType.Regular)
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
            LinkCaptured?.Invoke(this, new CapturedLink(payload.Url, source));
            await WriteTextAsync(ctx, 200, "queued").ConfigureAwait(false);
        }
        else
        {
            await WriteTextAsync(ctx, 502, "rejected: " + failure).ConfigureAwait(false);
        }
    }

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
    }

    public sealed class CapturedLink
    {
        public CapturedLink(string url, string source)
        {
            Url = url; Source = source;
        }
        public string Url { get; }
        public string Source { get; }
    }
}