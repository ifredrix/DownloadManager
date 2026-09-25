using System.Reflection;

namespace IfredrixDownloadManager;

/// <summary>
/// Creates/removes the optional Desktop shortcut. MSI no longer ships one
/// (so the installer needs no option dialog): the app asks once on first
/// run and owns the .lnk afterwards, including cleanup on uninstall.
/// </summary>
static class DesktopShortcut
{
    public const string FileName = "ifredrix Download Manager.lnk";

    public static string UserPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);

    public static string PublicPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), FileName);

    public static bool Exists() => File.Exists(UserPath) || File.Exists(PublicPath);

    /// <summary>Target of the shortcut: this exact executable.</summary>
    public static string TargetPath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "ifredrixDownloadManager.exe");

    public static bool Create()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;
            var lnk = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { UserPath });
            if (lnk == null) return false;
            var lnkType = lnk.GetType();
            lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk,
                new object[] { TargetPath });
            lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk,
                new object[] { Path.GetDirectoryName(TargetPath) ?? string.Empty });
            lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk,
                new object[] { TargetPath + ",0" });
            lnkType.InvokeMember("Description", BindingFlags.SetProperty, null, lnk,
                new object[] { "ifredrix Download Manager" });
            lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            return File.Exists(UserPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes the user-owned shortcut (best effort).</summary>
    public static void Delete()
    {
        try { if (File.Exists(UserPath)) File.Delete(UserPath); } catch { }
    }

    /// <summary>Removes every copy (uninstall path, may need elevation).</summary>
    public static void DeleteAll()
    {
        Delete();
        try { if (File.Exists(PublicPath)) File.Delete(PublicPath); } catch { }
    }
}
