using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Self-update dialog: manifest URL, check button, download with progress,
/// SHA256 verify, then run. Built in code (no designer file).
/// </summary>
public sealed class UpdateForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly AppSettings _settings;
    private readonly TextBox _txtUrl;
    private readonly Label _lblStatus;
    private readonly ProgressBar _progress;
    private readonly Button _btnCheck;
    private readonly Button _btnDownload;
    private readonly Button _btnClose;
    private CancellationTokenSource? _cts;
    private UpdateCheck.Release? _pending;

    public UpdateForm(AppSettings settings)
    {
        _settings = settings;

        Text = T("upd.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 270);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lblUrl = new Label { Text = T("upd.url"), AutoSize = true, Location = new Point(16, 18) };
        _txtUrl = new TextBox
        {
            Location = new Point(16, 38), Size = new Size(528, 23),
            BorderStyle = BorderStyle.FixedSingle, Text = _settings.UpdateUrl,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _btnCheck = new Button
        {
            Text = T("upd.check"), Location = new Point(16, 72), Size = new Size(130, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnCheck.Click += async (_, _) => await CheckAsync();

        _lblStatus = new Label
        {
            Text = string.Format(T("upd.current"), UpdateCheck.CurrentVersion()),
            AutoSize = true, Location = new Point(16, 112),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _progress = new ProgressBar
        {
            Location = new Point(16, 138), Size = new Size(528, 18),
            Minimum = 0, Maximum = 100,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var hint = new Label
        {
            Text = T("upd.hint"),
            AutoSize = true, Location = new Point(16, 164)
        };

        _btnDownload = new Button
        {
            Text = T("upd.downloadRun"), Location = new Point(330, 216), Size = new Size(128, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Enabled = false
        };
        _btnDownload.Click += async (_, _) => await DownloadAndRunAsync();

        _btnClose = new Button
        {
            Text = T("dlg.close"), Location = new Point(464, 216), Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnClose.Click += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            Close();
        };

        AcceptButton = _btnCheck;
        CancelButton = _btnClose;

        Controls.Add(lblUrl);
        Controls.Add(_txtUrl);
        Controls.Add(_btnCheck);
        Controls.Add(_lblStatus);
        Controls.Add(_progress);
        Controls.Add(hint);
        Controls.Add(_btnDownload);
        Controls.Add(_btnClose);

        FormClosing += (_, _) =>
        {
            _settings.UpdateUrl = _txtUrl.Text.Trim();
            try { _cts?.Cancel(); } catch { }
        };

        _btnCheck.Tag = "primary";
        Theme.StyleForm(this);
    }

    private async Task CheckAsync()
    {
        _btnCheck.Enabled = false;
        _btnDownload.Enabled = false;
        _pending = null;
        _lblStatus.Text = T("upd.checking");
        try
        {
            var url = _txtUrl.Text.Trim();
            _settings.UpdateUrl = url;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var release = await UpdateCheck.CheckAsync(url, cts.Token);
            var current = UpdateCheck.CurrentVersion();
            if (UpdateCheck.IsNewer(release.Version, current))
            {
                _pending = release;
                _lblStatus.Text = string.Format(T("upd.available"), release.Version, current) +
                                  (string.IsNullOrWhiteSpace(release.Notes) ? "" : "\n" + release.Notes);
                _btnDownload.Enabled = true;
            }
            else
            {
                _lblStatus.Text = string.Format(T("upd.latest"), current);
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
        }
        finally
        {
            _btnCheck.Enabled = true;
        }
    }

    private async Task DownloadAndRunAsync()
    {
        if (_pending == null) return;
        _cts = new CancellationTokenSource();
        _btnDownload.Enabled = false;
        _btnCheck.Enabled = false;
        _progress.Value = 0;
        try
        {
            var progress = new Progress<double>(pct =>
                _progress.Value = (int)Math.Clamp(Math.Round(pct), 0, 100));
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ifredrixDownloadManager", "updates");
            var file = await UpdateCheck.DownloadAsync(_pending, dir, progress, _cts.Token);
            _lblStatus.Text = string.Format(T("upd.saved"), file);

            var ans = MessageBox.Show(this, string.Format(T("upd.runNow"), file),
                T("upd.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }
                Application.Exit();
            }
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _btnCheck.Enabled = true;
            _btnDownload.Enabled = _pending != null;
        }
    }
}
