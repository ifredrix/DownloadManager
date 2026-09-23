using System;
using System.Net;

namespace IfredrixDownloadManager;

/// <summary>
/// Global connection settings: proxy mode plus shared cookie jar.
/// Applied to every HTTP transfer client (direct and Tor).
/// </summary>
public static class NetConfig
{
    /// <summary>None, System or Custom.</summary>
    public static string ProxyMode { get; set; } = "None";

    public static string ProxyHost { get; set; } = "127.0.0.1";
    public static int ProxyPort { get; set; } = 8080;
    public static string ProxyUser { get; set; } = string.Empty;
    public static string ProxyPass { get; set; } = string.Empty;

    /// <summary>
    /// Session cookies: the probe response can plant cookies that the
    /// segment downloads then send back (sites with cookie checks).
    /// </summary>
    public static readonly CookieContainer Cookies = new();

    /// <summary>Saved per-site logins (host -&gt; credentials), synced from settings.</summary>
    public static Dictionary<string, SiteLogin> SiteLogins { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static void ApplyProxy(System.Net.Http.SocketsHttpHandler handler)
    {
        if (string.Equals(ProxyMode, "Custom", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ProxyHost) && ProxyPort > 0 && ProxyPort < 65536)
        {
            var proxy = new WebProxy(ProxyHost, ProxyPort);
            if (!string.IsNullOrEmpty(ProxyUser))
            {
                proxy.Credentials = new NetworkCredential(ProxyUser, ProxyPass);
            }
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        else if (string.Equals(ProxyMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            handler.Proxy = null;
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }
    }

    /// <summary>Basic auth: URL userinfo first, then saved per-site logins.</summary>
    public static string? BasicAuthFor(Uri uri)
    {
        try
        {
            string? user = null;
            string? pass = null;
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                user = Uri.UnescapeDataString(parts[0]);
                pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            }
            else if (SiteLogins.TryGetValue(uri.Host, out var saved) &&
                     !string.IsNullOrEmpty(saved.User))
            {
                user = saved.User;
                pass = saved.Pass;
            }
            if (string.IsNullOrEmpty(user)) return null;
            return "Basic " + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(user + ":" + (pass ?? string.Empty)));
        }
        catch
        {
            return null;
        }
    }
}
