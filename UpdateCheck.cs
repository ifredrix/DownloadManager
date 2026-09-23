using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Self-update client. Reads either a generic manifest
/// ({version,url,sha256,notes}) or the GitHub Releases "latest" API.
/// Needs a distribution point (e.g. GitHub Releases); without a configured
/// URL the feature stays dormant. Downloads are SHA256-verified.
/// </summary>
public static class UpdateCheck
{
    public sealed class Release
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public static string CurrentVersion()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "0.0.0";
        }
    }

    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length && int.TryParse(new string(pa[i].TakeWhile(char.IsDigit).ToArray()), out var xi) ? xi : 0;
            var y = i < pb.Length && int.TryParse(new string(pb[i].TakeWhile(char.IsDigit).ToArray()), out var yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    public static bool IsNewer(string latest, string current) => CompareVersions(latest, current) > 0;

    public static async Task<Release> CheckAsync(string manifestUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException("No update URL configured.");
        }
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Update URL must be http(s).");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var json = await http.GetStringAsync(manifestUrl, ct).ConfigureAwait(false);

        if (manifestUrl.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGitHub(json);
        }
        return ParseGeneric(json);
    }

    private static Release ParseGeneric(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var release = new Release
        {
            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? string.Empty : string.Empty,
            DownloadUrl = root.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty,
            Sha256 = root.TryGetProperty("sha256", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            Notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? string.Empty : string.Empty
        };
        if (string.IsNullOrWhiteSpace(release.Version) || string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            throw new InvalidOperationException("Update manifest is missing version/url.");
        }
        return release;
    }

    private static Release ParseGitHub(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var version = tag.TrimStart('v', 'V');
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;

        string url = string.Empty;
        if (root.TryGetProperty("assets", out var assets))
        {
            string? fallback = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(assetUrl)) continue;
                fallback ??= assetUrl;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = assetUrl;
                    break;
                }
            }
            url ??= string.Empty;
            if (string.IsNullOrEmpty(url)) url = fallback ?? string.Empty;
        }
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("GitHub release has no usable version/asset.");
        }
        return new Release { Version = version, DownloadUrl = url, Notes = notes };
    }

    /// <summary>Downloads (progress 0-100) and SHA256-verifies when a hash is known.</summary>
    public static async Task<string> DownloadAsync(
        Release release, string destDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Refusing non-http(s) update URL.");
        }

        Directory.CreateDirectory(destDir);
        var name = uri.LocalPath.Split('/').Last();
        if (string.IsNullOrWhiteSpace(name)) name = "update.bin";
        var dest = Path.Combine(destDir, name);

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
            using var response = await http.GetAsync(release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            string actual;
            using (var fs = File.OpenRead(dest))
            {
                actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
            if (!string.Equals(actual, release.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                try { File.Delete(dest); } catch { }
                throw new InvalidOperationException("Update SHA256 mismatch - aborted.");
            }
        }

        return dest;
    }
}
