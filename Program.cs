using System.Net.Http;
using System.Text;
using System.Windows.Forms;

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

        // Background mode (autostart, tray life): show no window, the
        // tray icon is the UI. Plain second launches still show the window.
        var startMinimized = args.Any(a =>
            string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));
        var quit = args.Any(a =>
            string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-quit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/quit", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Another instance owns the GUI: hand links and files to it,
            // wake its window (it may be parked in the tray), then exit.
            // --quit instead asks it to shut down gracefully (saves history).
            if (quit) QuitRunningInstance();
            else
            {
                if (targets.Count > 0) ForwardToRunningInstance(targets);
                WakeRunningInstance();
            }
            return;
        }
        if (quit) return;

        // The app once vanished with no trace: route every fault path into
        // app.log. The unhandled mode must be set before the first window
        // exists, otherwise WinForms swallows UI faults silently.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Crash("AppDomain", e.ExceptionObject as Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Crash("UI thread", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Crash("Task", e.Exception);
            e.SetObserved();
        };

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        AppLog.Info($"start v{Application.ProductVersion} pid={Environment.ProcessId} targets={targets.Count}");
        if (!IsSupportedOs(out var osDetail))
        {
            AppLog.Error("unsupported OS: " + osDetail);
            MessageBox.Show(
                "ifredrix Download Manager membutuhkan Windows 10 versi 1607 (build 14393) atau lebih baru, 64-bit.\n" +
                "Perangkat ini: " + osDetail + ".\n\n" +
                "ifredrix Download Manager requires Windows 10 version 1607 (build 14393) or newer, 64-bit.\n" +
                "This device: " + osDetail + ".",
                "ifredrix Download Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var form = new MainForm();
        form.StartMinimized = startMinimized;
        form.StartupUrls.AddRange(targets);
        Application.Run(form);
        AppLog.Info("exit: main loop ended");
    }

    /// <summary>
    /// Gerbang kompatibilitas OS pengganti LaunchCondition MSI: memakai
    /// Environment.OSVersion (RtlGetVersion = angka asli, deterministik di
    /// semua mesin), bukan properti MSI yang terbukti menyesatkan.
    /// .NET 8 SCD butuh Windows 10 build 14393+ x64.
    /// </summary>
    private static bool IsSupportedOs(out string detail)
    {
        var v = Environment.OSVersion.Version;
        detail = "Windows " + v.Major + "." + v.Minor + " build " + v.Build +
            (Environment.Is64BitOperatingSystem ? " x64" : " x86");
        if (!Environment.Is64BitOperatingSystem) return false;
        if (v.Major < 10) return false;
        return v.Major > 10 || v.Build >= 14393;
    }

    /// <summary>Asks the running instance to shut down gracefully.</summary>
    private static void QuitRunningInstance()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var port in CapturePorts)
        {
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = http.PostAsync($"http://127.0.0.1:{port}/quit", content)
                    .GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) break;
            }
            catch
            {
                // Try the next port.
            }
        }
    }

    /// <summary>Asks the running instance to show its window (best effort).</summary>
    private static void WakeRunningInstance()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var port in CapturePorts)
        {
            try
            {
                http.GetAsync($"http://127.0.0.1:{port}/show").GetAwaiter().GetResult();
                return;
            }
            catch
            {
                // Try the next port.
            }
        }
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
