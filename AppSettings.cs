using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IfredrixDownloadManager;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Where finished downloads are written. Defaults to the per-user Downloads
    /// folder; only an explicit user setting overrides this.
    /// </summary>
    public string DownloadPath { get; set; } = DefaultDownloadPath();

    private static string DefaultDownloadPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public int MaxConcurrentDownloads { get; set; } = 3;
    public int ConnectionsPerDownload { get; set; } = DownloadManager.DefaultConnections;

    /// <summary>0 means unlimited. Stored in KB/s in the UI, bytes/s here.</summary>
    public long SpeedLimitBytesPerSecond { get; set; }

    public bool MonitorClipboard { get; set; }
    public bool AutoStartDownloads { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;

    // ------------------------------------------------------- scheduler ----
    // Queue scheduler: start/pause the queue at fixed times and
    // run an action once every tracked download reaches a final state.
    public bool ScheduleStartEnabled { get; set; }
    public string ScheduleStartTime { get; set; } = "02:00";
    public bool ScheduleStopEnabled { get; set; }
    public string ScheduleStopTime { get; set; } = "06:00";

    /// <summary>None, Shutdown, Sleep or Hibernate.</summary>
    public string ActionAfterAllDone { get; set; } = "None";

    // ------------------------------------------------------------ tor ----
    /// <summary>SOCKS endpoint of the local Tor client.</summary>
    public string TorHost { get; set; } = "127.0.0.1";

    public int TorPort { get; set; } = 9050;

    /// <summary>Route every HTTP(S) download through Tor.</summary>
    public bool TorRouteAll { get; set; }

    /// <summary>Off, External (use a running Tor) or Managed (launch tor.exe).</summary>
    public string TorMode { get; set; } = "Managed";

    // ---------------------------------------------------------- network ----
    /// <summary>None, System or Custom proxy for HTTP transfers.</summary>
    public string ProxyMode { get; set; } = "None";

    public string ProxyHost { get; set; } = "127.0.0.1";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUser { get; set; } = string.Empty;

    /// <summary>Stored in plain text like most download managers.</summary>
    public string ProxyPass { get; set; } = string.Empty;

    /// <summary>Start with Windows (HKCU Run entry, applied on Save).</summary>
    public bool AutoStart { get; set; }

    /// <summary>Show the New Download dialog for URL-box adds.</summary>
    public bool ConfirmNewDownload { get; set; } = true;

    /// <summary>Desktop-icon question already answered (asked once).</summary>
    public bool DesktopIconAsked { get; set; }

    /// <summary>UI language: "id" or "en".</summary>
    public string Language { get; set; } = "id";

    /// <summary>Self-update manifest URL (generic JSON or GitHub Releases API).</summary>
    public string UpdateUrl { get; set; } = "https://raw.githubusercontent.com/ifredrix/DownloadManager/main/update/latest.json";

    /// <summary>URL exclusion patterns, one per line (# = comment).</summary>
    public string ExcludedPatterns { get; set; } = string.Empty;

    /// <summary>Saved per-site logins, keyed by host (sent as Basic auth).</summary>
    public Dictionary<string, SiteLogin> SiteLogins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------- categories ----
    /// <summary>
    /// Per-category save folders. Empty/missing means the main download path.
    /// Keys: Video, Audio, Archive, Document, Application, Image, Torrent,
    /// Streams, Other.
    /// </summary>
    public Dictionary<string, string> CategoryDirs { get; set; } = new();

    /// <summary>Custom tor.exe path; empty means auto-detect.</summary>
    public string TorExePath { get; set; } = string.Empty;

    /// <summary>
    /// Light or Dark skin. Legacy "Breathing" values are migrated to "Light".
    /// WinForms palettes live in <see cref="Theme"/>.
    /// </summary>
    public string ThemeMode { get; set; } = "Light";

    /// <summary>Blue, Green, Purple or Orange.</summary>
    public string AccentName { get; set; } = "Blue";

    /// <summary>Main-grid column layout (order, width, visibility).</summary>
    public List<GridColumnState> GridColumns { get; set; } = GridColumnState.Defaults();

    /// <summary>Top-toolbar button keys, left-to-right display order.</summary>
    public List<string> ToolbarOrder { get; set; } = new() { "AddUrl", "AddTorrent", "Stream", "GrabLinks", "Browsers", "Settings" };

    /// <summary>Top-toolbar button keys the user hid.</summary>
    public List<string> ToolbarHidden { get; set; } = new();

    /// <summary>Slowly rotating RGB halo around the window buttons (-2 px ring).</summary>
    public bool RgbRings { get; set; } = true;

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ifredrixDownloadManager");

    [JsonIgnore]
    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    [JsonIgnore]
    public static string HistoryFile => Path.Combine(SettingsDirectory, "history.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
                if (loaded != null)
                {
                    // Migrate legacy values.
                    if (string.Equals(loaded.ThemeMode, "Breathing", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(loaded.ThemeMode))
                    {
                        loaded.ThemeMode = "Light";
                    }
                    if (loaded.GridColumns == null || loaded.GridColumns.Count == 0)
                    {
                        loaded.GridColumns = GridColumnState.Defaults();
                    }
                    if (loaded.ToolbarOrder == null || loaded.ToolbarOrder.Count == 0)
                    {
                        loaded.ToolbarOrder = new() { "AddUrl", "AddTorrent", "Stream", "GrabLinks", "Browsers", "Settings" };
                    }
                    loaded.ToolbarHidden ??= new();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt file used to reset everything silently (download path,
            // Tor mode). Repair from the .bak instead; when that fails the
            // broken file is only renamed, never deleted.
            AppLog.Error("settings load failed: " + ex.Message);
            var repaired = TryRepairFromBackup();
            if (repaired != null) return repaired;
            PreserveCorruptSettingsFile();
            AppLog.Error("settings unreadable and no usable backup: original kept as .corrupt, starting with defaults");
        }

        return new AppSettings();
    }

    private static AppSettings? TryRepairFromBackup()
    {
        try
        {
            var bak = SettingsFile + ".bak";
            if (!File.Exists(bak)) return null;
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(bak));
            if (loaded == null) return null;
            File.Copy(bak, SettingsFile, true);
            AppLog.Info("settings repaired from settings.json.bak");
            return loaded;
        }
        catch (Exception ex)
        {
            AppLog.Error("settings backup unusable: " + ex.Message);
            return null;
        }
    }

    private static void PreserveCorruptSettingsFile()
    {
        try
        {
            if (File.Exists(SettingsFile)) File.Move(SettingsFile, SettingsFile + ".corrupt", true);
        }
        catch (Exception ex)
        {
            AppLog.Error("preserving corrupt settings failed: " + ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            // Keep the last good file: Load repairs from it if the new copy
            // ever turns up unreadable.
            if (File.Exists(SettingsFile))
            {
                try { File.Copy(SettingsFile, SettingsFile + ".bak", true); } catch { }
            }
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception ex)
        {
            // Settings are best-effort.
            AppLog.Error("settings save failed: " + ex);
        }
    }
}

/// <summary>Saved login for one site (password in plain text, like most managers).</summary>
public sealed class SiteLogin
{
    public string User { get; set; } = string.Empty;
    public string Pass { get; set; } = string.Empty;
}

/// <summary>Persisted main-grid column layout.</summary>
public sealed class GridColumnState
{
    public string Name { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public int Width { get; set; } = 100;
    public int DisplayIndex { get; set; }

    public static List<GridColumnState> Defaults() => new()
    {
        new GridColumnState { Name = "FileName", Visible = true, Width = 260, DisplayIndex = 0 },
        new GridColumnState { Name = "Size", Visible = true, Width = 80, DisplayIndex = 1 },
        new GridColumnState { Name = "Progress", Visible = true, Width = 150, DisplayIndex = 2 },
        new GridColumnState { Name = "Speed", Visible = true, Width = 95, DisplayIndex = 3 },
        new GridColumnState { Name = "ETA", Visible = true, Width = 70, DisplayIndex = 4 },
        new GridColumnState { Name = "Status", Visible = true, Width = 90, DisplayIndex = 5 },
        new GridColumnState { Name = "Type", Visible = true, Width = 70, DisplayIndex = 6 },
        new GridColumnState { Name = "Category", Visible = true, Width = 85, DisplayIndex = 7 },
    };
}
