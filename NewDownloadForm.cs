using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// "Save As" confirmation before queueing:
/// file name, folder, plus probed size/resume info.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class NewDownloadForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly DownloadManager.ProbedDownload _probed;
    private readonly TextBox _txtUrl;
    private readonly TextBox _txtFile;
    private readonly TextBox _txtDir;
    private readonly Button _btnBrowse;
    private readonly Button _ok;
    private readonly Button _cancel;

    public NewDownloadForm(DownloadManager.ProbedDownload probed)
    {
        _probed = probed;

        Text = T("dl.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 260);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lblUrl = new Label { Text = T("dl.url"), AutoSize = true, Location = new Point(16, 16) };
        _txtUrl = new TextBox
        {
            Location = new Point(16, 36), Size = new Size(488, 23),
            ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Text = probed.Source,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var lblFile = new Label { Text = T("dl.file"), AutoSize = true, Location = new Point(16, 68) };
        _txtFile = new TextBox
        {
            Location = new Point(16, 88), Size = new Size(488, 23),
            BorderStyle = BorderStyle.FixedSingle, Text = probed.FileName,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var lblDir = new Label { Text = T("dl.saveIn"), AutoSize = true, Location = new Point(16, 120) };
        _txtDir = new TextBox
        {
            Location = new Point(16, 140), Size = new Size(388, 23),
            BorderStyle = BorderStyle.FixedSingle, Text = probed.DefaultDir,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _btnBrowse = new Button
        {
            Text = T("dlg.browse"), Location = new Point(410, 139), Size = new Size(94, 25),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnBrowse.Click += (_, _) => BrowseDir();

        var info = new Label
        {
            Text = string.Format(T("dl.info"), FormatSize(probed.FileSize),
                probed.SupportsRange ? T("pr.yes") : T("pr.no")),
            AutoSize = true,
            Location = new Point(16, 172)
        };

        _ok = new Button
        {
            Text = T("dlg.ok"), Location = new Point(318, 216), Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _ok.Click += (_, _) => Accept();
        _cancel = new Button
        {
            Text = T("dlg.cancel"), Location = new Point(414, 216), Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(lblUrl);
        Controls.Add(_txtUrl);
        Controls.Add(lblFile);
        Controls.Add(_txtFile);
        Controls.Add(lblDir);
        Controls.Add(_txtDir);
        Controls.Add(_btnBrowse);
        Controls.Add(info);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return T("pr.none");
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private void BrowseDir()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T("dl.saveIn"),
            UseDescriptionForTitle = true
        };
        try
        {
            if (Directory.Exists(_txtDir.Text)) dialog.SelectedPath = _txtDir.Text;
        }
        catch
        {
            // Ignore a bad current path.
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _txtDir.Text = dialog.SelectedPath;
        }
    }

    private void Accept()
    {
        var name = _txtFile.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, T("dl.needName"), T("dl.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dir = _txtDir.Text.Trim();
        try
        {
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException(T("msg.noFolder"));
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(T("msg.badFolder"), ex.Message), T("msg.settingsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _probed.FileName = name;
        _probed.DefaultDir = dir;
        _probed.CustomDir = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
