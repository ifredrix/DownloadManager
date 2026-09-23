using System;
using Microsoft.Win32;

namespace IfredrixDownloadManager;

/// <summary>
/// Per-user .torrent association (HKCU\Software\Classes, no admin needed):
/// double-clicking a .torrent file opens this app with the file.
/// </summary>
public static class FileAssoc
{
    public const string Extension = ".torrent";
    public const string ProgId = "ifredrixDownloadManager.torrent";

    private const string ClassesRoot = @"Software\Classes";

    public static bool IsAssociated()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ClassesRoot + "\\" + Extension, writable: false);
            return string.Equals(key?.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetAssociated(bool associated)
    {
        if (associated)
        {
            // Stash any previous mapping so untoggling restores it.
            try
            {
                using var existing = Registry.CurrentUser.OpenSubKey(ClassesRoot + "\\" + Extension, writable: false);
                var prev = existing?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(prev) &&
                    !string.Equals(prev, ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    using var backup = Registry.CurrentUser.CreateSubKey(@"Software\ifredrixDownloadManager\AssocBackup");
                    backup?.SetValue("torrent", prev);
                }
            }
            catch
            {
                // Best effort.
            }

            using var ext = Registry.CurrentUser.CreateSubKey(ClassesRoot + "\\" + Extension);
            ext?.SetValue(null, ProgId);

            using var prog = Registry.CurrentUser.CreateSubKey(ClassesRoot + "\\" + ProgId);
            prog?.SetValue(null, "Torrent file (ifredrix Download Manager)");

            using var cmd = Registry.CurrentUser.CreateSubKey(
                ClassesRoot + "\\" + ProgId + @"\shell\open\command");
            cmd?.SetValue(null, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" \"%1\"");

            using var icon = Registry.CurrentUser.CreateSubKey(
                ClassesRoot + "\\" + ProgId + @"\DefaultIcon");
            icon?.SetValue(null, "\"" + System.Windows.Forms.Application.ExecutablePath + "\",0");

            // Let Explorer pick the change up without relogin.
            NativeRefresh();
        }
        else
        {
            try
            {
                var current = Registry.CurrentUser.OpenSubKey(ClassesRoot + "\\" + Extension, writable: false)
                    ?.GetValue(null) as string;
                if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + "\\" + ProgId, throwOnMissingSubKey: false);
                    string? backup = null;
                    try
                    {
                        using var backupKey = Registry.CurrentUser.OpenSubKey(
                            @"Software\ifredrixDownloadManager\AssocBackup", writable: false);
                        backup = backupKey?.GetValue("torrent") as string;
                    }
                    catch
                    {
                        // No backup stored.
                    }
                    if (!string.IsNullOrWhiteSpace(backup))
                    {
                        using var ext = Registry.CurrentUser.CreateSubKey(ClassesRoot + "\\" + Extension);
                        ext?.SetValue(null, backup);
                    }
                    else
                    {
                        using var ext = Registry.CurrentUser.OpenSubKey(ClassesRoot + "\\" + Extension, writable: true);
                        string? defaultValue = null;
                        ext?.DeleteValue(defaultValue!, throwOnMissingValue: false);
                    }
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            @"Software\ifredrixDownloadManager\AssocBackup", throwOnMissingSubKey: false);
                    }
                    catch
                    {
                        // Best effort.
                    }
                }
                NativeRefresh();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void NativeRefresh()
    {
        try
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Non-fatal: Explorer refreshes on next login at the latest.
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
