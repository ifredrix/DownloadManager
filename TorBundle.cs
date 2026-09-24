using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// One-click Tor expert bundle updater (Tor setup dialog).
/// Safety rules: HTTPS-only, official torproject.org hosts only, SHA256 from
/// the official checksum file must match (no silent downgrades, no install
/// on any mismatch), and everything is user-initiated.
/// </summary>
public static class TorBundle
{
    private const string DownloadPage = "https://www.torproject.org/download/tor";

    private static readonly string[] AllowedHosts =
        { "www.torproject.org", "torproject.org", "archive.torproject.org", "dist.torproject.org" };

    public static string BundleDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ifredrixDownloadManager", "tools", "tor-bundle");

    public static string BundledExePath
    {
        get
        {
            // Newer expert bundles nest everything (tor/tor.exe, data/,
            // docs/) instead of one flat tree: accept either layout.
            var flat = Path.Combine(BundleDirectory, "tor.exe");
            if (File.Exists(flat)) return flat;
            var nested = Path.Combine(BundleDirectory, "tor", "tor.exe");
            if (File.Exists(nested)) return nested;
            return flat;
        }
    }

    public sealed class ReleaseInfo
    {
        public string BrowserVersion { get; set; } = string.Empty;
        public string TorVersion { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string SumsUrl { get; set; } = string.Empty;
    }

    /// <summary>Reads the official download page for the latest stable bundle.</summary>
    public static async Task<ReleaseInfo> CheckLatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");

        string html;
        try
        {
            html = await http.GetStringAsync(DownloadPage, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not reach torproject.org: " + ex.Message);
        }

        // tor-expert-bundle-windows-x86_64-15.0.23.tar.gz
        // (page links point at the .asc signature; the archive is the same
        // URL minus ".asc"). Take the newest STABLE (alphas contain "a").
        ReleaseInfo? best = null;
        foreach (Match m in Regex.Matches(html,
            @"href\s*=\s*[""']([^""']*tor-expert-bundle-windows-x86_64-([\d.a]+)\.tar\.gz)(?:\.asc)?[""']",
            RegexOptions.IgnoreCase))
        {
            var version = m.Groups[2].Value;
            if (version.Contains('a')) continue; // alpha/beta, skip
            var candidate = new ReleaseInfo
            {
                FileUrl = Absolute(DownloadPage, m.Groups[1].Value),
                BrowserVersion = version
            };
            if (candidate.FileUrl.EndsWith(".asc", StringComparison.OrdinalIgnoreCase))
            {
                candidate.FileUrl = candidate.FileUrl[..^4];
            }
            AssertOfficial(candidate.FileUrl);
            candidate.FileName = candidate.FileUrl.Split('/').Last();
            if (best == null || CompareVersions(candidate.BrowserVersion, best.BrowserVersion) > 0)
            {
                best = candidate;
            }
        }
        if (best == null)
        {
            throw new InvalidOperationException(
                "Could not parse the download page (layout changed). Check " + DownloadPage + " manually.");
        }
        var info = best;

        var torMatch = Regex.Match(html, @"\(tor\s+([\d.]+)\)");
        if (torMatch.Success) info.TorVersion = torMatch.Groups[1].Value;

        // Official checksum file lives next to the artifacts.
        var dir = info.FileUrl[..(info.FileUrl.LastIndexOf('/') + 1)];
        info.SumsUrl = dir + "sha256sums-unsigned-build.txt";
        AssertOfficial(info.SumsUrl);

        return info;
    }

    private static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
            var y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Local tor version via `tor --version`, or null when missing.</summary>
    public static async Task<string?> LocalVersionAsync(string torExe)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = torExe,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p == null) return null;
            if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return null; }
            var output = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var m = Regex.Match(output, @"Tor version ([\d.]+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downloads, SHA256-verifies and extracts the bundle. Returns tor.exe.</summary>
    public static async Task<string> InstallAsync(
        ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(release.SumsUrl))
        {
            throw new InvalidOperationException(
                "No official checksum file found - refusing to install unverified binaries.");
        }

        Directory.CreateDirectory(BundleDirectory);
        var archive = Path.Combine(BundleDirectory, release.FileName + ".download");

        var phase = "download";
        try
        {
            await DownloadToFileAsync(release.FileUrl, archive, progress, ct).ConfigureAwait(false);

            phase = "checksum verify";
            var expected = await LookupHashAsync(release, ct).ConfigureAwait(false);
            var actual = await Task.Run(() =>
            {
                using var fs = File.OpenRead(archive);
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }, ct).ConfigureAwait(false);

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SHA256 mismatch - the download may be tampered with. Aborted.");
            }

            phase = "extract";
            var stage = Path.Combine(BundleDirectory, "stage");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);

            await Task.Run(() =>
            {
                using var fs = File.OpenRead(archive);
                using var gzip = new GZipStream(fs, CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                TarEntry? entry;
                while ((entry = tar.GetNextEntry()) != null)
                {
                    if (entry.EntryType != TarEntryType.RegularFile) continue;
                    var dest = Path.GetFullPath(Path.Combine(stage, entry.Name));
                    if (!dest.StartsWith(stage, StringComparison.OrdinalIgnoreCase)) continue; // traversal guard
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var outFile = File.Create(dest);
                    entry.DataStream!.CopyTo(outFile);
                }
            }, ct).ConfigureAwait(false);

            var torExe = Directory.EnumerateFiles(stage, "tor.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("tor.exe not found in the bundle.");
            phase = "replace";

            // Replace the previous bundle: clear the directory (keeping the
            // archive, deleted in finally), then move the extracted tree in.
            // tor.exe, its DLLs and geoip data end up side by side.
            var root = FirstRoot(stage);
            foreach (var path in Directory.GetFileSystemEntries(BundleDirectory))
            {
                if (string.Equals(path, archive, StringComparison.OrdinalIgnoreCase)) continue;
                // Never wipe the stage directory we just extracted into -
                // doing so deletes the fresh files, and every step below
                // then fails with "could not find a part of the path".
                if (string.Equals(path, stage, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else File.Delete(path);
                }
                catch
                {
                    // Best effort; a locked tor.exe below will surface as missing.
                }
            }
            foreach (var dir in Directory.GetDirectories(root))
            {
                Directory.Move(dir, Path.Combine(BundleDirectory, Path.GetFileName(dir)));
            }
            foreach (var file in Directory.GetFiles(root))
            {
                var dest = Path.Combine(BundleDirectory, Path.GetFileName(file));
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);
            }

            // Newer bundles nest tor.exe (tor/tor.exe); older ones land
            // flat. Accept wherever it landed and report the real path so
            // settings adopt it instead of a location that stays missing.
            var finalExe = Directory.EnumerateFiles(BundleDirectory, "tor.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Install finished but tor.exe is missing.");
            return finalExe;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Tor bundle {phase} failed" +
                (phase == "replace"
                    ? " (if files vanished mid-install, check antivirus quarantine and whitelist the tools folder)"
                    : string.Empty) +
                ": " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
            try { if (Directory.Exists(Path.Combine(BundleDirectory, "stage"))) Directory.Delete(Path.Combine(BundleDirectory, "stage"), true); } catch { }
        }
    }

    private static string FirstRoot(string stage)
    {
        var dirs = Directory.GetDirectories(stage);
        // Bundles usually wrap everything in one top folder; otherwise use stage itself.
        if (dirs.Length == 1 && Directory.GetFiles(stage).Length == 0) return dirs[0];
        return stage;
    }

    private static async Task<string> LookupHashAsync(ReleaseInfo release, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
        var sums = await http.GetStringAsync(release.SumsUrl, ct).ConfigureAwait(false);
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].TrimStart('*').Equals(release.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].Trim().ToLowerInvariant();
            }
        }
        throw new InvalidOperationException("Checksum for this file is not listed - refusing to install.");
    }

    private static async Task DownloadToFileAsync(
        string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        AssertOfficial(url);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
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
            if (total > 0) progress?.Report(done * 100.0 / total);
        }
    }

    private static string Absolute(string baseUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return new Uri(new Uri(baseUrl), href).ToString();
    }

    private static void AssertOfficial(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing non-official URL: " + url);
        }
    }
}
