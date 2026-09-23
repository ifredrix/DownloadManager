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
    /// <summary>Auto format: best MP4 video + audio, merged to MP4 (needs ffmpeg).</summary>
    public const string BestMp4Format = "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";

    /// <summary>Fallback when ffmpeg is missing: best single MP4 file, no merge.</summary>
    public const string FallbackMp4Format = "b[ext=mp4]/b/best";

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

    /// <summary>
    /// Returns a usable ffmpeg path, downloading the bundled copy on first use.
    /// Needed for video+audio merges and TS -&gt; MP4 remux.
    /// </summary>
    public async Task<string> EnsureFfmpegAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var existing = ResolveFfmpeg();
        if (existing != null) return existing;

        var toolsDir = Path.GetDirectoryName(BundledFfmpegPath)!;
        Directory.CreateDirectory(toolsDir);

        var zip = Path.Combine(toolsDir, "ffmpeg.zip");
        var extractDir = Path.Combine(toolsDir, "ffmpeg-dl");
        try
        {
            await DownloadToFileAsync(FfmpegDownloadUrl, zip, progress, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, extractDir);

            var exe = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            if (exe == null) throw new InvalidOperationException("ffmpeg.exe not found in archive.");

            if (File.Exists(BundledFfmpegPath)) File.Delete(BundledFfmpegPath);
            File.Move(exe, BundledFfmpegPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Could not fetch ffmpeg: " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
        }

        _ffmpegProbed = false;
        _ffmpegPath = null;
        return await Task.FromResult(ResolveFfmpeg()
            ?? throw new InvalidOperationException("Could not install ffmpeg."));
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

    public async Task<List<StreamFormat>> ListFormatsAsync(string url, CancellationToken ct = default)
    {
        var tool = ResolveTool();
        if (tool == null) throw new FileNotFoundException(
            "yt-dlp is not available. Use Get yt-dlp in this dialog to fetch it.");

        var output = new List<string>();
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = tool,
            Arguments = $"--no-warnings --no-playlist -F \"{url}\"",
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
            if (!Regex.IsMatch(id, @"^\d+[a-z]?$")) continue;

            var rest = line[(firstSpace + 1)..];

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

    /// <summary>Downloads the chosen format. Returns the path of the produced file.</summary>
    public async Task<string?> DownloadAsync(
        string url, string formatId, string destinationDirectory,
        IProgress<StreamProgress>? progress, CancellationToken ct, long speedLimitBps = 0)
    {
        var tool = ResolveTool();
        if (tool == null) throw new FileNotFoundException(
            "yt-dlp is not available. Add any stream link once in the app to auto-fetch it.");

        Directory.CreateDirectory(destinationDirectory);

        var title = await ProbeTitleAsync(url, ct).ConfigureAwait(false);
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

        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = tool,
            Arguments = $"--no-warnings --no-playlist --continue -f \"{formatId}\" " +
                        $"-P \"{destinationDirectory}\" " +
                        mergeArgs + ffmpegArgs + rateArgs +
                        $"-o \"%(title).150s [{Sanitize(formatId)}].%(ext)s\" " +
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

        if (p.ExitCode != 0 && string.IsNullOrEmpty(lastPath) &&
            System.Text.RegularExpressions.Regex.IsMatch(formatId, @"^[\w-]+$"))
        {
            // Try to find the file in the destination folder by the chosen format id.
            lastPath = Directory.EnumerateFiles(destinationDirectory, $"*[{formatId}]*")
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(lastPath))
        {
            lastPath = Directory.EnumerateFiles(destinationDirectory)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(lastPath) || !File.Exists(lastPath))
        {
            var tail = string.Join(Environment.NewLine, logTail.TakeLast(8));
            throw new InvalidOperationException(
                $"yt-dlp failed (exit {p.ExitCode})." +
                (string.IsNullOrWhiteSpace(tail) ? string.Empty : Environment.NewLine + tail));
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

    public Task<string> GetTitleAsync(string url, CancellationToken ct = default)
        => ProbeTitleAsync(url, ct);

    private async Task<string> ProbeTitleAsync(string url, CancellationToken ct)
    {
        var tool = ResolveTool();
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = tool!,
                Arguments = $"--no-warnings --no-playlist --print title -- \"{url}\"",
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