using System.Net.Http;
using System.Text;

namespace IfredrixDownloadManager;

static class Program
{
    private const string MutexName = @"Local\ifredrixDownloadManager";
    private static readonly int[] CapturePorts = { 8795, 8796, 8797, 8798 };

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        var targets = args
            .Where(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith('-') && !a.StartsWith('/'))
            .Select(a => a.Trim().Trim('"'))
            .Where(a => a.Contains("://", StringComparison.Ordinal) ||
                        a.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                        (a.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(a)))
            .ToList();

        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Another instance owns the GUI: hand links and files to it, then exit.
            if (targets.Count > 0) ForwardToRunningInstance(targets);
            return;
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        form.StartupUrls.AddRange(targets);
        Application.Run(form);
    }

    /// <summary>Sends links and .torrent files to the running instance.</summary>
    private static void ForwardToRunningInstance(List<string> targets)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var target in targets)
        {
            if (target.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                ForwardFile(http, target);
            }
            else if (Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                ForwardUrl(http, target);
            }
        }
    }

    private static void ForwardUrl(HttpClient http, string url)
    {
        var json = "{\"url\":" + JsonString(url) + ",\"source\":\"cli\"}";
        foreach (var port in CapturePorts)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = http.PostAsync($"http://127.0.0.1:{port}/capture?source=cli", content)
                    .GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) break;
            }
            catch
            {
                // Try the next port.
            }
        }
    }

    private static void ForwardFile(HttpClient http, string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.Length > 100 * 1024 * 1024) return;
        }
        catch
        {
            return;
        }

        var query = Uri.EscapeDataString(Path.GetFileName(path));
        foreach (var port in CapturePorts)
        {
            try
            {
                using var content = new ByteArrayContent(bytes);
                var response = http.PostAsync($"http://127.0.0.1:{port}/capture-file?source=cli&filename=" + query, content)
                    .GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) break;
            }
            catch
            {
                // Try the next port.
            }
        }
    }

    private static string JsonString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
