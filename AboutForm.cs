using System;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// About dialog. MessageBox can only draw the stock Windows icon, which is
/// why the app icon never appeared here; a regular form picks up
/// Theme.AppIcon like every other window.
/// </summary>
public sealed class AboutForm : Form
{
    private static string T(string key) => Localization.T(key);

    public AboutForm()
    {
        Text = T("msg.aboutTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 240);
        Font = new Font("Segoe UI", 9F);
        BackColor = Theme.WindowBack;
        ForeColor = Theme.Text;
        Icon = Theme.AppIcon;

        var iconBox = new PictureBox
        {
            Image = Theme.AppIcon.ToBitmap(),
            Location = new Point(16, 16),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };

        var title = new Label
        {
            Text = "ifredrix Download Manager " + UpdateCheck.CurrentVersion() + "-beta",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            ForeColor = Theme.Text,
            Location = new Point(78, 18),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "HTTP segmented + Torrent + Streams + Tor",
            ForeColor = Theme.TextMuted,
            Location = new Point(79, 47),
            AutoSize = true
        };

        var yt = new StreamCapture().ResolveTool() ?? T("msg.notInstalled");
        var ff = new StreamCapture().ResolveFfmpeg() ?? T("msg.notInstalled");
        var details = new Label
        {
            Text = $"yt-dlp: {yt}\nffmpeg: {ff}\nTor: {TorProxy.Host}:{TorProxy.Port}",
            ForeColor = Theme.Text,
            Location = new Point(16, 84),
            AutoSize = true
        };

        var license = new Label
        {
            Text = "MIT License",
            ForeColor = Theme.TextMuted,
            Location = new Point(16, 166),
            AutoSize = true
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(379, 198),
            Size = new Size(75, 30),
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        ok.FlatAppearance.BorderSize = 0;
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange(new Control[] { iconBox, title, subtitle, details, license, ok });
    }
}
