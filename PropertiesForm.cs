using System;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Per-download details, live-refreshed.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class PropertiesForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly DownloadManager _manager;
    private readonly string _taskId;
    private readonly System.Windows.Forms.Timer _timer;

    private readonly TextBox _txtUrl;
    private readonly TextBox _txtPath;
    private readonly Label _lblFile;
    private readonly Label _lblType;
    private readonly Label _lblCategory;
    private readonly Label _lblStatus;
    private readonly Label _lblSize;
    private readonly Label _lblDownloaded;
    private readonly Label _lblSpeed;
    private readonly Label _lblLimit;
    private readonly Label _lblEta;
    private readonly Label _lblCreated;
    private readonly Label _lblCompleted;
    private readonly Label _lblError;
    private readonly Label _lblTor;
    private readonly Label _lblResume;
    private readonly ProgressBar _progress;

    public PropertiesForm(DownloadManager manager, string taskId)
    {
        _manager = manager;
        _taskId = taskId;

        Text = T("pr.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 470);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var y = 14;
        _lblFile = AddRow(T("pr.file"), ref y, bold: true);
        _txtUrl = AddBox(T("pr.url"), ref y);
        _txtPath = AddBox(T("pr.saveAs"), ref y);
        _lblType = AddRow(T("pr.type"), ref y);
        _lblCategory = AddRow(T("pr.category"), ref y);
        _lblStatus = AddRow(T("pr.status"), ref y);
        _lblSize = AddRow(T("pr.size"), ref y);
        _lblDownloaded = AddRow(T("pr.downloaded"), ref y);

        _progress = new ProgressBar
        {
            Location = new Point(140, y),
            Size = new Size(404, 16),
            Minimum = 0,
            Maximum = 1000,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_progress);
        y += 24;

        _lblSpeed = AddRow(T("pr.speed"), ref y);
        _lblLimit = AddRow(T("pr.limit"), ref y);
        _lblEta = AddRow(T("pr.eta"), ref y);
        _lblCreated = AddRow(T("pr.created"), ref y);
        _lblCompleted = AddRow(T("pr.completed"), ref y);
        _lblTor = AddRow(T("pr.tor"), ref y);
        _lblResume = AddRow(T("pr.resume"), ref y);
        _lblError = AddRow(T("pr.error"), ref y);

        var btnCopyUrl = new Button
        {
            Text = T("pr.copyUrl"),
            Location = new Point(140, y),
            Size = new Size(100, 28),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        btnCopyUrl.Click += (_, _) =>
        {
            try { Clipboard.SetText(_txtUrl.Text); } catch { }
        };
        var btnClose = new Button
        {
            Text = T("dlg.close"),
            Location = new Point(454, y),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnCopyUrl);
        Controls.Add(btnClose);
        AcceptButton = btnClose;
        CancelButton = btnClose;
        ClientSize = new Size(560, y + 42);

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RefreshValues();
        RefreshValues();
        _timer.Start();
        FormClosing += (_, _) => _timer.Stop();

        btnClose.Tag = "primary";
        Theme.StyleForm(this);
    }

    private Label AddRow(string caption, ref int y, bool bold = false)
    {
        var lbl = new Label
        {
            Text = caption,
            Location = new Point(16, y),
            Size = new Size(112, 17),
            AutoSize = false
        };
        var value = new Label
        {
            Location = new Point(140, y),
            Size = new Size(404, 17),
            AutoSize = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        if (bold) value.Font = new Font(value.Font, FontStyle.Bold);
        Controls.Add(lbl);
        Controls.Add(value);
        y += 23;
        return value;
    }

    private TextBox AddBox(string caption, ref int y)
    {
        var lbl = new Label
        {
            Text = caption,
            Location = new Point(16, y + 2),
            Size = new Size(112, 17),
            AutoSize = false
        };
        var box = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(404, 23),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(lbl);
        Controls.Add(box);
        y += 29;
        return box;
    }

    private void RefreshValues()
    {
        var task = _manager.GetTask(_taskId);
        if (task == null)
        {
            Close();
            return;
        }

        Text = T("pr.title") + " - " + task.FileName;
        _lblFile.Text = task.FileName;
        if (_txtUrl.Text != task.Url) _txtUrl.Text = task.Url;
        if (_txtPath.Text != task.SavePath) _txtPath.Text = task.SavePath;
        _lblType.Text = task.TypeText;
        _lblCategory.Text = task.CategoryText;
        _lblStatus.Text = task.StatusText + (string.IsNullOrEmpty(task.ErrorMessage) ? "" : $" ({task.ErrorMessage})");
        _lblSize.Text = task.SizeText;
        _lblDownloaded.Text = task.FileSize > 0
            ? string.Format(T("pr.of"), task.FormattedDownloadedBytes, task.FormattedFileSize, task.ProgressPercentage)
            : task.FormattedDownloadedBytes;
        _progress.Value = (int)Math.Clamp(Math.Round(task.ProgressPercentage * 10), 0, 1000);
        _lblSpeed.Text = string.Format(T("pr.downUp"), task.FormattedDownloadSpeed, task.FormattedUploadSpeed);
        _lblLimit.Text = task.SpeedLimitBps > 0
            ? string.Format(T("pr.limitThis"), task.SpeedLimitBps / 1024.0)
            : T("pr.limitGlobal");
        _lblEta.Text = task.FormattedTimeRemaining;
        _lblCreated.Text = task.FormattedCreatedAt;
        _lblCompleted.Text = task.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? T("pr.none");
        _lblTor.Text = task.UseTor ? T("pr.yes") : T("pr.no");
        _lblResume.Text = task.SupportsRange ? T("pr.yes") : T("pr.no");
        _lblError.Text = string.IsNullOrEmpty(task.ErrorMessage) ? T("pr.none") : task.ErrorMessage;
    }
}
