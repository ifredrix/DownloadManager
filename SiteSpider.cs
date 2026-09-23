using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Lightweight site spider: fetches a page, extracts
/// links, and optionally crawls one level of same-site pages. Results go to
/// the link picker so the user chooses what enters the queue.
/// </summary>
public static class SiteSpider
{
    private static readonly Regex LinkRegex =
        new(@"(?:href|src)\s*=\s*[""']([^""'#]+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SrcSetRegex =
        new(@"srcset\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".m3u8", ".mpd",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma",
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".epub",
        ".exe", ".msi", ".apk", ".dmg", ".deb", ".rpm",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp",
        ".torrent"
    };

    private const int MaxPages = 20;
    private const int MaxLinks = 2000;

    public static bool LooksLikeFile(string url)
    {
        try
        {
            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme == Uri.UriSchemeFtp) return true;
            var ext = System.IO.Path.GetExtension(uri.LocalPath);
            if (FileExtensions.Contains(ext)) return true;
            return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<string>> CrawlAsync(
        string pageUrl, int depth, bool filesOnly,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, int Level)>();
        queue.Enqueue((pageUrl, 0));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");

        while (queue.Count > 0 && visited.Count < MaxPages)
        {
            var (current, level) = queue.Dequeue();
            if (!visited.Add(current)) continue;
            ct.ThrowIfCancellationRequested();

            string html;
            try
            {
                log?.Report("Fetching: " + current);
                html = await http.GetStringAsync(current, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Report("Skipped (" + ex.Message + "): " + current);
                continue;
            }

            foreach (var link in ExtractLinks(html, current))
            {
                if (found.Count >= MaxLinks) break;
                if (!IsQueueable(link)) continue;
                if (filesOnly && !LooksLikeFile(link))
                {
                    // Candidate for deeper crawl if same site.
                    if (level < depth && IsSameHost(link, pageUrl) && IsHtmlPage(link))
                    {
                        queue.Enqueue((link, level + 1));
                    }
                    continue;
                }
                found.Add(link);
                if (level < depth && !filesOnly && IsSameHost(link, pageUrl) && IsHtmlPage(link))
                {
                    queue.Enqueue((link, level + 1));
                }
            }
        }

        return found.OrderBy(u => u).ToList();
    }

    public static List<string> ExtractLinks(string html, string baseUrl)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(html)) return results;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return results;

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            raw = raw.Trim();
            if (raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return;
            if (raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return;
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            if (raw.StartsWith("#")) return;
            try
            {
                var absolute = new Uri(baseUri, raw);
                if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps &&
                    absolute.Scheme != Uri.UriSchemeFtp && absolute.Scheme != "magnet") return;
                results.Add(Uri.UnescapeDataString(absolute.ToString()));
            }
            catch
            {
                // Skip unresolvable references.
            }
        }

        foreach (Match m in LinkRegex.Matches(html)) Add(m.Groups[1].Value);
        foreach (Match m in SrcSetRegex.Matches(html))
        {
            var first = m.Groups[1].Value.Split(',')[0].Trim().Split(' ')[0];
            Add(first);
        }
        foreach (Match m in Regex.Matches(html, @"magnet:\?[^\s""'<>]+", RegexOptions.IgnoreCase))
        {
            results.Add(m.Value);
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsQueueable(string url)
    {
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == Uri.UriSchemeFtp);
    }

    private static bool IsSameHost(string url, string root)
    {
        try
        {
            return string.Equals(new Uri(url).Host, new Uri(root).Host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHtmlPage(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme == Uri.UriSchemeFtp) return false;
            var ext = System.IO.Path.GetExtension(uri.LocalPath);
            return string.IsNullOrEmpty(ext) || ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".php", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".aspx", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
