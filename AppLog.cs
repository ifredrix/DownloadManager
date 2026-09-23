using System.Text;

namespace IfredrixDownloadManager;

/// <summary>
/// Append-only diagnostic log in the settings folder. The app once died
/// without a trace (no handler, no file), so unhandled exceptions and
/// lifecycle milestones land in app.log. Writing never throws: losing a log
/// line beats taking the process down while handling a fault.
/// </summary>
internal static class AppLog
{
    private static readonly object Sync = new();
    private const long MaxBytes = 1024 * 1024;

    // Same folder as settings.json / history.json (kept in sync manually so
    // AppLog has no static-initializer dependency on AppSettings).
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ifredrixDownloadManager", "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Crash(string source, Exception? exception) =>
        Write("FATAL", source + ": " + (exception?.ToString() ?? "unknown exception"));

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // One rotated copy: a crash loop must not fill the disk.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Copy(FilePath, FilePath + ".old", true);
                    File.WriteAllText(FilePath, string.Empty);
                }

                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                           " [" + level + "] " + message + Environment.NewLine;
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
