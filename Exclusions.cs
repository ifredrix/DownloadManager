using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IfredrixDownloadManager;

/// <summary>
/// URL exclusion list: wildcard patterns,
/// one per line. Lines starting with # are comments. Matched links are
/// ignored by automatic capture (clipboard watcher, browser extension)
/// but can still be added manually.
/// </summary>
public static class Exclusions
{
    public static List<string> Parse(string? text)
    {
        var patterns = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return patterns;
        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            patterns.Add(line);
        }
        return patterns;
    }

    public static bool IsExcluded(string url, string? patternsText)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(patternsText)) return false;
        foreach (var pattern in Parse(patternsText))
        {
            if (Matches(pattern, url)) return true;
        }
        return false;
    }

    public static bool Matches(string pattern, string url)
    {
        try
        {
            // Wildcards: * = any run, ? = one char. Everything else literal.
            var regex = "^" + Regex.Escape(pattern)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";
            return Regex.IsMatch(url, regex,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }
}
