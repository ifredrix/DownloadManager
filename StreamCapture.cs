using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Wraps an external <c>yt-dlp</c> tool to enumerate formats and download a
/// stream up to 8K (4320p) when available.
/// </summary>
public sealed class StreamCapture
{
    /// <summary>Auto format: forces 4K/8K video + preferred audio when the source has it ("jika bisa"), merged to MP4 (needs ffmpeg).</summary>
    // Prefer 4K/8K video (any container) + preferred audio when the source has
    // it ("jika bisa"), then fall back to the previous best-MP4 chain. "+" forms
    // are merged by yt-dlp with --merge-output-format mp4 (see RunDownloadAsync).
    public const string BestMp4Format =
        "bv*[height>=2160]+ba[ext=m4a]/bv*[height>=2160]+ba/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";

    /// <summary>Fallback when ffmpeg is missing: best single MP4 file, no merge.</summary>
    public const string FallbackMp4Format = "b[ext=mp4]/b/best";

    /// <summary>Strips characters that would break out of a quoted argument
    /// or split an HTTP header (values come from browsers and cookie
    /// stores, never from the keyboard - defense in depth).</summary>
    private static string H(string value) =>
        (value ?? string.Empty).Replace("\"", string.Empty)
            .Replace("\r", string.Empty).Replace("\n", string.Empty)
            .Replace("\t", string.Empty);

    /// <summary>
    /// Optional `--referer` plus the browser's User-Agent for yt-dlp.
    /// Cookies go through a temp Netscape jar (--cookies file) instead of
    /// --add-header: yt-dlp then matches them per domain, so a session
    /// cookie is never forwarded to a CDN or redirect it does not belong to.
    /// </summary>
    private static string ContextArgs(string referer, string ua)
    {
        var args = string.Empty;
        if (!string.IsNullOrWhiteSpace(referer)) args += "--referer \"" + H(referer) + "\" ";
        if (!string.IsNullOrWhiteSpace(ua)) args += "--add-header \"User-Agent: " + H(ua) + "\" ";
        return args;
    }

    /// <summary>Temp Netscape cookie jar for one yt-dlp call; deleted on dispose.</summary>
    private sealed class CookieJar : IDisposable
    {
        private readonly string _path;
        public string Arg { get; }

        public CookieJar(List<CookieEntry>? cookies)
        {
            _path = string.Empty;
            Arg = string.Empty;
            if (cookies == null || cookies.Count == 0) return;
            try
            {
                var lines = new List<string>
                {
                    "# Netscape HTTP Cookie File",
                    "# ifredrix Download Manager - temporary jar, deleted after this call"
                };
                var sessionExpiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 43200;
                foreach (var c in cookies)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    var domain = (c.Domain ?? string.Empty).Trim().TrimStart('.');
                    if (domain.Length == 0) continue;
                    lines.Add(
                        (c.HostOnly ? domain : "." + domain) + "\t" +
                        (c.HostOnly ? "FALSE" : "TRUE") + "\t" +
                        (string.IsNullOrEmpty(c.Path) ? "/" : c.Path) + "\t" +
                        (c.Secure ? "TRUE" : "FALSE") + "\t" +
                        (c.Expires > 0 ? (long)c.Expires : sessionExpiry) + "\t" +
                        c.Name + "\t" + H(c.Value));
                }
                if (lines.Count <= 2) return;
                _path = Path.Combine(Path.GetTempPath(),
                    "ifredrix-cookies-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllLines(_path, lines);
                Arg = "--cookies \"" + _path + "\" ";
            }
            catch { _path = string.Empty; Arg = string.Empty; }
        }

        public void Dispose()
        {
            try { if (_path.Length > 0) File.Delete(_path); } catch { /* best effort */ }
        }
    }

    /// <summary>Per-app bundled copy, fetched on first use (no separate install).</summary>
    public static string BundledToolPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ifredrixDownloadManager", "tools", "yt-dlp.exe");

    public const string ToolDownloadUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    /// <summary>Bundled ffmpeg for merges and TS -&gt; MP4 remux.</summary>
    public static string BundledFfmpegPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ifredrixDownloadManager", "tools", "ffmpeg.exe");

    public const string FfmpegDownloadUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static bool IsStreamingUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host.ToLowerInvariant();
        return host.EndsWith("youtube.com") || host.EndsWith("youtu.be") ||
               host.EndsWith("vimeo.com") || host.EndsWith("twitch.tv") ||
               host.EndsWith("tiktok.com") || host.EndsWith("dailymotion.com") ||
               host.EndsWith("soundcloud.com") || host.EndsWith("twitter.com") ||
               host.EndsWith("x.com") || host.EndsWith("facebook.com") ||
               host.EndsWith("reddit.com") || host.EndsWith("bandcamp.com") ||
               host.EndsWith("nicovideo.jp") || host.EndsWith("bilibili.com");
    }

    /// <summary>
    /// True for plain progressive files (by path extension): the
    /// multi-connection range downloader handles them faster than yt-dlp and
    /// now carries the same browser headers. Callers check stream hosts and
    /// playlists first, so .m3u8/.mpd deliberately stay out of this list.
    /// </summary>
    public static bool IsDirectFileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var path = u.AbsolutePath;
        foreach (var ext in DirectFileExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static readonly string[] DirectFileExtensions =
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".flv", ".wmv",
        ".mpg", ".mpeg", ".3gp", ".mp3", ".m4a", ".aac", ".ogg", ".opus",
        ".flac", ".wav", ".wma", ".zip", ".rar", ".7z", ".exe", ".msi",
        ".pdf", ".iso", ".gz", ".tar"
    };

    /// <summary>Direct HLS/DASH playlist links convert to MP4 via yt-dlp.</summary>
    public static bool IsPlaylistUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            var path = uri.LocalPath;
            return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    private static readonly Regex ResolutionRegex =
        new(@"(\d{2,5})x(\d{2,5})", RegexOptions.Compiled);

    private static readonly Regex SizeRegex =
        new(@"(\d+(?:\.\d+)?)\s*(KiB|MiB|GiB|TiB)", RegexOptions.Compiled);

    private static readonly Regex PercentRegex =
        new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);

    private static readonly Regex DestRegex =
        new(@"^\[Merger\] Merging formats into ""(?<path>.+?)""",
            RegexOptions.Compiled);

    private string? _toolPath;

    public string? ToolPath => _toolPath;

    public bool IsAvailable => _toolPath != null;

    /// <summary>Tries bundled copy first, then PATH / Python scripts dirs.</summary>
    public string? ResolveTool()
    {
        if (_toolPath != null) return _toolPath;

        try
        {
            if (File.Exists(BundledToolPath) &&
                new FileInfo(BundledToolPath).Length > 1024 * 1024)
            {
                _toolPath = BundledToolPath;
                return _toolPath;
            }
        }
        catch
        {
            // Fall through to PATH lookup.
        }

        foreach (var exe in new[] { "yt-dlp.exe", "yt-dlp", "youtube-dl.exe", "youtube-dl" })
        {
            var resolved = WhereOnPath(exe);
            if (resolved != null) { _toolPath = resolved; return resolved; }
        }

        // Fall back to Python's Scripts folder.
        var pythonRoot = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                         + @"\Programs\Python";
        if (Directory.Exists(pythonRoot))
        {
            foreach (var scripts in Directory.EnumerateDirectories(pythonRoot, "Scripts", SearchOption.AllDirectories))
            {
                foreach (var exe in new[] { "yt-dlp.exe", "youtube-dl.exe" })
                {
                    var full = Path.Combine(scripts, exe);
                    if (File.Exists(full)) { _toolPath = full; return full; }
                }
            }
        }

        _toolPath = null;
        return null;
    }

    /// <summary>
    /// Returns a usable yt-dlp path, downloading the bundled copy on first use.
    /// Reports 0-100 download progress when a fetch is needed.
    /// </summary>
    public async Task<string> EnsureToolAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var existing = ResolveTool();
        if (existing != null) return existing;

        var dest = BundledToolPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var tmp = dest + ".download";
        try
        {
            await DownloadToFileAsync(ToolDownloadUrl, tmp, progress, ct).ConfigureAwait(false);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw new InvalidOperationException(
                "Could not download yt-dlp. Check your connection and try again, " +
                "or install it manually with 'pip install yt-dlp'.");
        }

        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not install yt-dlp: " + ex.Message);
        }

        _toolPath = dest;
        return dest;
    }

    private static async Task DownloadToFileAsync(
        string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
        using var response = await http.GetAsync(
            url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var net = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[256 * 1024];
        long done = 0;
        int read;
        while ((read = await net.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total > 0)
            {
                progress?.Report(done * 100.0 / total);
            }
        }
    }

    private string? _ffmpegPath;
    private bool _ffmpegProbed;

    /// <summary>Locates ffmpeg (bundled copy, then PATH). No download.</summary>
    public string? ResolveFfmpeg()
    {
        if (_ffmpegPath != null) return _ffmpegPath;
        if (_ffmpegProbed) return null;
        _ffmpegProbed = true;

        try
        {
            if (File.Exists(BundledFfmpegPath) &&
                new FileInfo(BundledFfmpegPath).Length > 1024 * 1024)
            {
                _ffmpegPath = BundledFfmpegPath;
                return _ffmpegPath;
            }
        }
        catch
        {
            // Fall through to PATH lookup.
        }

        var onPath = WhereOnPath("ffmpeg.exe") ?? WhereOnPath("ffmpeg");
        if (onPath != null) _ffmpegPath = onPath;
        return _ffmpegPath;
    }

    /// <summary>Mirrors for the ffmpeg build; the first one that answers wins.
    /// Measured on the target connection: BtbN ~0.9 MB/s vs gyan ~0.1 MB/s,
    /// so GitHub is the primary and gyan.dev the fallback.</summary>
    public static readonly string[] FfmpegDownloadUrls =
    {
        "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    };

    private bool _ffmpegFetchFailed;

    /// <summary>True when a fetch already failed this session, so every
    /// queued task does not burn bandwidth trying again.</summary>
    public bool FfmpegUnavailable => _ffmpegFetchFailed;

    /// <summary>
    /// Returns a usable ffmpeg path, downloading the bundled copy on first use.
    /// Needed for video+audio merges and TS -&gt; MP4 remux.
    /// </summary>
    public async Task<string> EnsureFfmpegAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var existing = ResolveFfmpeg();
        if (existing != null) return existing;
        if (_ffmpegFetchFailed)
        {
            throw new InvalidOperationException("ffmpeg is not available on this machine.");
        }

        var toolsDir = Path.GetDirectoryName(BundledFfmpegPath)!;
        Directory.CreateDirectory(toolsDir);

        var zip = Path.Combine(toolsDir, "ffmpeg.zip");
        var extractDir = Path.Combine(toolsDir, "ffmpeg-dl");
        Exception? last = null;

        foreach (var url in FfmpegDownloadUrls)
        {
            try
            {
                await DownloadToFileAsync(url, zip, progress, ct).ConfigureAwait(false);

                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, extractDir);

                var exe = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (exe == null) throw new InvalidOperationException("ffmpeg.exe not found in archive.");

                if (File.Exists(BundledFfmpegPath)) File.Delete(BundledFfmpegPath);
                File.Move(exe, BundledFfmpegPath);

                // ffprobe rides along; yt-dlp probes media with it when present.
                var ffprobe = Directory.EnumerateFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (ffprobe != null)
                {
                    try
                    {
                        File.Copy(ffprobe, Path.Combine(toolsDir, "ffprobe.exe"), true);
                    }
                    catch { /* best effort */ }
                }

                _ffmpegProbed = false;
                _ffmpegPath = null;
                _ffmpegFetchFailed = false;
                return ResolveFfmpeg()
                    ?? throw new InvalidOperationException("Could not install ffmpeg.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
            }
        }

        _ffmpegFetchFailed = true;
        throw new InvalidOperationException(
            "Could not fetch ffmpeg: " + (last?.Message ?? "no mirror answered."));
    }

    private static string? WhereOnPath(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p == null) return null;
            if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } return null; }
            if (p.ExitCode != 0) return null;
            var first = p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => !string.IsNullOrEmpty(l));
            return first;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<StreamFormat>> ListFormatsAsync(
        string url, CancellationToken ct = default, string referer = "",
        string ua = "", List<CookieEntry>? cookies = null)
    {
        var tool = ResolveTool();
        if (tool == null) throw new FileNotFoundException(
            "yt-dlp is not available. Use Get yt-dlp in this dialog to fetch it.");

        var output = new List<string>();
        using var jar = new CookieJar(cookies);
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = tool,
            Arguments = $"--no-warnings --no-playlist {ContextArgs(referer, ua)}{jar.Arg}-F \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start yt-dlp.");

        p.OutputDataReceived += (_, e) => { if (e.Data != null) output.Add(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) output.Add(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        // Honest failure: a broken/404 link must surface the real error
        // instead of an empty list that looks like "no formats exist".
        if (p.ExitCode != 0)
        {
            var tail = string.Join(Environment.NewLine, output.TakeLast(6));
            throw new InvalidOperationException(
                $"yt-dlp failed (exit {p.ExitCode})." +
                (string.IsNullOrWhiteSpace(tail) ? string.Empty : Environment.NewLine + tail));
        }

        return ParseFormats(output);
    }

    private static List<StreamFormat> ParseFormats(List<string> lines)
    {
        var result = new List<StreamFormat>();
        var inFormats = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;

            if (line.Contains("Available formats"))
            {
                inFormats = true;
                continue;
            }
            if (!inFormats) continue;
            if (line.StartsWith("[") || line.StartsWith("-") || line.StartsWith("ID")) continue;

            // Format lines start with a numeric ID. Anything else is a separator.
            var firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0) continue;
            var id = line[..firstSpace];
            // Numeric yt-dlp ids ("399", "251a") and generic single-word ids
            // ("mp4" for direct file links) are both valid format selectors.
            if (!Regex.IsMatch(id, @"^(\d+[a-z]?|[a-z][a-z0-9._-]*)$",
                    RegexOptions.IgnoreCase)) continue;

            // Trim the column padding: short IDs leave several spaces before
            // EXT, and a leading space made the regex below fail (ext became
            // "?" and rows displayed junk like "?+mp4").
            var rest = line[(firstSpace + 1)..].TrimStart();

            var extMatch = Regex.Match(rest, @"^(\S+)\s+(.+)$");
            var ext = extMatch.Success ? extMatch.Groups[1].Value : "?";
            var description = extMatch.Success ? extMatch.Groups[2].Value : rest;

            int height = 0;
            var res = ResolutionRegex.Match(description);
            if (res.Success)
            {
                height = int.Parse(res.Groups[2].Value);
            }

            var sizeMatch = SizeRegex.Match(description);
            var size = sizeMatch.Success ? sizeMatch.Value : string.Empty;

            bool isAudio = description.Contains("audio only", StringComparison.OrdinalIgnoreCase);
            int fps = 0;
            var fpsMatch = Regex.Match(description, @"(\d+)\s*fps");
            if (fpsMatch.Success) fps = int.Parse(fpsMatch.Groups[1].Value);

            result.Add(new StreamFormat
            {
                Id = id,
                Extension = ext,
                Resolution = res.Success ? res.Value : (isAudio ? "audio" : "?"),
                Height = height,
                Fps = fps,
                Size = size,
                IsAudio = isAudio,
                IsVideo = !isAudio && res.Success
            });
        }
        return result;
    }

    /// <summary>
    /// Builds the selectable quality list for the browser panel: one combined
    /// video+audio choice per resolution (needs ffmpeg to merge) plus the
    /// best audio-only tracks. Sizes are summed so the number shown is what
    /// the merged file will actually weigh.
    /// </summary>
    public async Task<List<StreamChoice>> ListChoicesAsync(
        string url, CancellationToken ct = default, string referer = "",
        string ua = "", List<CookieEntry>? cookies = null)
    {
        var formats = await ListFormatsAsync(url, ct, referer, ua, cookies).ConfigureAwait(false);
        var choices = new List<StreamChoice>();
        if (formats.Count == 0) return choices;

        var audios = formats.Where(f => f.IsAudio)
            .OrderByDescending(f => ParseSizeBytes(f.Size)).ToList();

        // Keep containers consistent: mp4 video marries m4a (aac), webm
        // video marries webm/opus - the merged file then plays everywhere.
        StreamFormat? AudioFor(StreamFormat video)
        {
            if (audios.Count == 0) return null;
            var want = string.Equals(video.Extension, "mp4", StringComparison.OrdinalIgnoreCase)
                ? "m4a" : "webm";
            return audios.FirstOrDefault(a =>
                       string.Equals(a.Extension, want, StringComparison.OrdinalIgnoreCase))
                   ?? audios[0];
        }

        foreach (var group in formats.Where(f => f.IsVideo && f.Height > 0)
                     .GroupBy(f => f.Height)
                     .OrderByDescending(g => g.Key))
        {
            var v = group
                .OrderBy(f => string.Equals(f.Extension, "mp4", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(f => f.Fps)
                .ThenByDescending(f => ParseSizeBytes(f.Size))
                .First();

            var audio = AudioFor(v);
            var id = v.Id;
            // "?" = EXT column was not recognised for that row (unknown).
            var ext = v.Extension == "?" ? string.Empty : v.Extension;
            var size = ParseSizeBytes(v.Size);
            if (audio != null)
            {
                id += "+" + audio.Id;
                var audioExt = audio.Extension == "?" ? string.Empty : audio.Extension;
                if (ext.Length == 0)
                {
                    ext = audioExt;
                }
                else if (audioExt.Length > 0 &&
                         !ext.Equals(audioExt, StringComparison.OrdinalIgnoreCase))
                {
                    // Different containers (mp4+m4a): show both. Equal ones
                    // (mp4+mp4) would just be noise on the panel's sub-line.
                    ext += "+" + audioExt;
                }
                var audioSize = ParseSizeBytes(audio.Size);
                if (size > 0 && audioSize > 0) size += audioSize;
            }

            choices.Add(new StreamChoice
            {
                Id = id,
                Label = v.Fps > 30 ? $"{v.Height}p{v.Fps}" : $"{v.Height}p",
                Ext = ext,
                SizeBytes = size,
                Kind = "video"
            });
        }

        // Resolution-less rows still matter (direct files, muxed streams).
        if (choices.Count == 0)
        {
            foreach (var f in formats.Where(f => !f.IsAudio && f.Height == 0).Take(3))
            {
                choices.Add(new StreamChoice
                {
                    Id = f.Id,
                    Label = f.Extension,
                    Ext = f.Extension,
                    SizeBytes = ParseSizeBytes(f.Size),
                    Kind = "video"
                });
            }
        }

        foreach (var a in audios.Take(2))
        {
            choices.Add(new StreamChoice
            {
                Id = a.Id,
                Label = string.Empty,
                Ext = a.Extension,
                SizeBytes = ParseSizeBytes(a.Size),
                Kind = "audio"
            });
        }

        return choices;
    }

    /// <summary>Parses yt-dlp size strings ("177.98MiB") to bytes. 0 = unknown.</summary>
    public static long ParseSizeBytes(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 0;
        var m = Regex.Match(size.Trim(), @"^([\d.]+)\s*([KMGTP]i?B)$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return 0;
        }
        var unit = m.Groups[2].Value.ToUpperInvariant();
        var exponent = unit.StartsWith("K") ? 1 : unit.StartsWith("M") ? 2 :
                       unit.StartsWith("G") ? 3 : unit.StartsWith("T") ? 4 :
                       unit.StartsWith("P") ? 5 : 0;
        return (long)(value * Math.Pow(1024, exponent));
    }

    /// <summary>Deletes raw ".fNNN" track files a failed merge left behind.</summary>
    public static int PurgeFragments(string directory)
    {
        var removed = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!Regex.IsMatch(Path.GetFileName(file), @"\.[fF]\d+\.\w+$")) continue;
                try { File.Delete(file); removed++; } catch { /* locked */ }
            }
        }
        catch { /* folder gone */ }
        return removed;
    }

    /// <summary>Downloads the chosen format. Returns the path of the produced file.</summary>
    public async Task<string?> DownloadAsync(
        string url, string formatId, string destinationDirectory,
        IProgress<StreamProgress>? progress, CancellationToken ct, long speedLimitBps = 0,
        string referer = "", string ua = "", List<CookieEntry>? cookies = null)
    {
        var tool = ResolveTool();
        if (tool == null) throw new FileNotFoundException(
            "yt-dlp is not available. Add any stream link once in the app to auto-fetch it.");

        Directory.CreateDirectory(destinationDirectory);

        // Combined video+audio selectors must fail fast when ffmpeg is
        // missing: that lets the caller fetch ffmpeg and retry (or fall back
        // to a single file) *before* two separate track files get downloaded
        // and mistaken for a finished download.
        if (formatId.Contains('+') && ResolveFfmpeg() == null)
        {
            throw new InvalidOperationException(
                "ffmpeg is required to merge video and audio.");
        }

        var title = await ProbeTitleAsync(url, ct, referer, ua, cookies).ConfigureAwait(false);
        var safeTitle = Sanitize(title);

        // Merge flag only makes sense for combined (video+audio) selectors;
        // single-file fallbacks must not require ffmpeg. When ffmpeg is
        // available, point yt-dlp at it and remux everything to MP4
        // (this also converts TS segments instead of leaving .ts files).
        var ffmpeg = ResolveFfmpeg();
        var mergeArgs = formatId.Contains('+') ? "--merge-output-format mp4 " : string.Empty;
        var ffmpegArgs = ffmpeg != null
            ? $"--ffmpeg-location \"{Path.GetDirectoryName(ffmpeg)}\" --remux-video mp4 "
            : string.Empty;
        var rateArgs = speedLimitBps > 0 ? $"--limit-rate {FormatRate(speedLimitBps)} " : string.Empty;

        // The auto (best) selector is a long fallback chain - embedding it
        // verbatim would produce unreadable file names like
        // "title [bv_[height_=2160]+...].mp4", so label it "best".
        var formatLabel = formatId == BestMp4Format ? "best" : Sanitize(formatId);

        using var jar = new CookieJar(cookies);
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = tool,
            Arguments = $"--no-warnings --no-playlist {ContextArgs(referer, ua)}{jar.Arg}--continue -f \"{formatId}\" " +
                        $"-P \"{destinationDirectory}\" " +
                        mergeArgs + ffmpegArgs + rateArgs +
                        $"-o \"%(title).150s [{formatLabel}].%(ext)s\" " +
                        "--newline " +
                        $"\"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start yt-dlp.");

        using var killOnCancel = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });

        var lastPath = (string?)null;
        var logTail = new Queue<string>();
        void Note(string line)
        {
            lock (logTail)
            {
                logTail.Enqueue(line);
                while (logTail.Count > 15) logTail.Dequeue();
            }
        }
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Note(e.Data);
            var m = DestRegex.Match(e.Data);
            if (m.Success) lastPath = m.Groups["path"].Value;

            var pm = PercentRegex.Match(e.Data);
            if (pm.Success && double.TryParse(pm.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                progress?.Report(new StreamProgress(pct, e.Data));
            }
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Note(e.Data);
            var pm = PercentRegex.Match(e.Data);
            if (pm.Success && double.TryParse(pm.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                progress?.Report(new StreamProgress(pct, e.Data));
            }
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        string FailureTail()
        {
            lock (logTail)
            {
                return string.Join(Environment.NewLine, logTail.TakeLast(8));
            }
        }

        static bool IsTrackFragment(string path) =>
            Regex.IsMatch(Path.GetFileName(path), @"\.[fF]\d+\.\w+$");

        // A non-zero exit is a failed download, full stop. Accepting whatever
        // file happens to be the biggest (the old behaviour) reported merged
        // failures as "Completed" while leaving a video-only fragment behind.
        if (p.ExitCode != 0)
        {
            var tail = FailureTail();
            if (formatId.Contains('+') &&
                (tail.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                 tail.Contains("merg", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "ffmpeg is required to merge video and audio." + Environment.NewLine + tail);
            }
            throw new InvalidOperationException(
                $"yt-dlp failed (exit {p.ExitCode})." + Environment.NewLine + tail);
        }

        if (string.IsNullOrEmpty(lastPath))
        {
            lastPath = Directory.EnumerateFiles(destinationDirectory)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) &&
                            !IsTrackFragment(f))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(lastPath) || !File.Exists(lastPath))
        {
            throw new InvalidOperationException(
                "yt-dlp finished but produced no file." + Environment.NewLine + FailureTail());
        }

        // e.g. ".f399.mp4" + ".f140.m4a" means the merger never ran: never
        // report a half-finished download as completed.
        if (IsTrackFragment(lastPath))
        {
            throw new InvalidOperationException(
                "yt-dlp left separate video/audio track files; the merge did not run: " +
                Path.GetFileName(lastPath));
        }

        _ = safeTitle; // kept for future template tweaks
        return lastPath;
    }

    /// <summary>Downloads the best MP4 (video+audio) without asking. Returns the produced file.</summary>
    public Task<string?> DownloadBestAsync(
        string url, string destinationDirectory,
        IProgress<StreamProgress>? progress, CancellationToken ct, long speedLimitBps = 0)
        => DownloadAsync(url, BestMp4Format, destinationDirectory, progress, ct, speedLimitBps);

    private static string FormatRate(long bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024.0 * 1024):F1}M";
        }
        return $"{Math.Max(1, bytesPerSecond / 1024)}K";
    }

    public Task<string> GetTitleAsync(
        string url, CancellationToken ct = default, string referer = "",
        string ua = "", List<CookieEntry>? cookies = null)
        => ProbeTitleAsync(url, ct, referer, ua, cookies);

    private async Task<string> ProbeTitleAsync(
        string url, CancellationToken ct, string referer = "",
        string ua = "", List<CookieEntry>? cookies = null)
    {
        var tool = ResolveTool();
        try
        {
            using var jar = new CookieJar(cookies);
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = tool!,
                Arguments = $"--no-warnings --no-playlist {ContextArgs(referer, ua)}{jar.Arg}--print title -- \"{url}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p == null) return "stream";
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            var title = (await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
            return string.IsNullOrEmpty(title) ? "stream" : title;
        }
        catch
        {
            return "stream";
        }
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "stream";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.');
    }
}

/// <summary>One option in the browser panel's quality/size list.</summary>
public sealed class StreamChoice
{
    /// <summary>yt-dlp selector, e.g. "399+140" (merge) or "251" (audio).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Short label: "1080p60". Empty for audio rows.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Container(s), e.g. "mp4+m4a".</summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>Expected size in bytes (video+audio summed). 0 = unknown.</summary>
    public long SizeBytes { get; set; }

    /// <summary>"video" or "audio".</summary>
    public string Kind { get; set; } = "video";
}

public sealed class StreamFormat
{
    public string Id { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public int Height { get; set; }
    public int Fps { get; set; }
    public string Size { get; set; } = string.Empty;
    public bool IsAudio { get; set; }
    public bool IsVideo { get; set; }

    public override string ToString()
    {
        var suffix = !string.IsNullOrEmpty(Size) ? $"  {Size}" : "";
        var rate = Fps > 0 ? $" {Fps}fps" : "";
        return IsAudio
            ? $"{Id,-4} {Extension,-5} audio only{suffix}"
            : $"{Id,-4} {Extension,-5} {Resolution}{rate}{suffix}";
    }
}

public sealed class StreamProgress
{
    public StreamProgress(double percent, string line)
    {
        Percent = percent; Line = line;
    }
    public double Percent { get; }
    public string Line { get; }
}