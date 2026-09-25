using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Uninstall stub shipped as [INSTALLFOLDER]Uninstall.exe plus a Start Menu
// shortcut, so "uninstall from Windows" works even for users who never open
// Settings > Apps. It finds this product by its STABLE UpgradeCode and hands
// off to msiexec with full UI (UAC appears for the per-machine product).
// ProductCode is intentionally NOT hardcoded: WiX assigns a fresh one per
// build (major upgrades); the UpgradeCode below mirrors installer/product.wxs
// and must stay in sync with it.
static class Uninstaller
{
    private const string UpgradeCode = "A7C2E9D4-5B31-4F8E-9C60-2D1E84F30B75";

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiEnumRelatedProducts(
        string strUpgradeCode, int dwReserved, int iProductIndex, StringBuilder strProductCode);

    [STAThread]
    private static int Main()
    {
        var found = FindProduct();
        if (found == null)
        {
            MessageBox.Show(
                "ifredrix Download Manager is not installed (no product found for this installer).",
                "ifredrix Download Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        // The Desktop icon is app-owned (first-run prompt), not an MSI
        // component, so remove it here; msiexec handles the rest.
        // Best effort only: a missing file or denied folder never blocks
        // the uninstall (msiexec still runs below).
        foreach (var dir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        })
        {
            try
            {
                var lnk = Path.Combine(dir, "ifredrix Download Manager.lnk");
                if (!string.IsNullOrWhiteSpace(dir) && File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = "/x " + found,
            UseShellExecute = true,
        });
        return 0;
    }

    /// <summary>First installed product for our UpgradeCode, or null.</summary>
    private static string? FindProduct()
    {
        var product = new StringBuilder(39);
        // MSI takes the UpgradeCode WITH braces; without them every call
        // fails with ERROR_INVALID_PARAMETER and nothing is ever found.
        var upgradeBraced = "{" + UpgradeCode + "}";
        var index = 0;
        while (true)
        {
            if (index >= 16) return null;
            product.Clear();
            if (MsiEnumRelatedProducts(upgradeBraced, 0, index, product) != 0) return null;
            if (product.Length == 0) return null;
            // Major-upgrade regime: at most one installed instance.
            return product.ToString();
        }
    }
}
