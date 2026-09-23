using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Per-download progress window: live info,
/// per-connection segment bar + table, speed cap tab and on-done options.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class ProgressForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly DownloadManager _manager;
    private readonly string _taskId;
    private readonly System.Windows.Forms.Timer _timer;

    private readonly TextBox _txtUrl;
    private readonly Label _lblStatus;
    private readonly Label _lblSize;
    private readonly Label _lblDownloaded;
    private readonly Label _lblRate;
    private readonly Label _lblEta;
    private readonly Label _lblResume;
    private readonly ProgressBar _bar;
    private readonly Button _btnHide;
    private readonly Button _btnPause;
    private readonly Button _btnCancel;
    private readonly Label _lblDetailCaption;
    private readonly Panel _segBar;
    private readonly DataGridView _grid;
    private readonly NumericUpDown _nudLimit;
    private readonly Label _lblLimitCurrent;
    private readonly CheckBox _chkCloseDone;
    private readonly CheckBox _chkOpenFolderDone;

    private readonly int _expandedHeight;
    private bool _detailsVisible = true;
    private bool _doneActionsFired;

    public ProgressForm(DownloadManager manager, string taskId)
    {
        _manager = manager;
        _taskId = taskId;

        Text = "…";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 420);
        ClientSize = new Size(640, 600);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var tabs = new TabControl
        {
            Location = new Point(8, 8),
            Size = new Size(624, 584),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        var pageStatus = new TabPage(T("prog.statusTab"));
        var pageSpeed = new TabPage(T("prog.speedTab"));
        var pageOptions = new TabPage(T("prog.optionsTab"));
        tabs.TabPages.Add(pageStatus);
        tabs.TabPages.Add(pageSpeed);
        tabs.TabPages.Add(pageOptions);
        Controls.Add(tabs);

        // ---- status page ----
        _txtUrl = new TextBox
        {
            Location = new Point(10, 10), Size = new Size(596, 23),
            ReadOnly = true, BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        pageStatus.Controls.Add(_txtUrl);

        var y = 42;
        _lblStatus = InfoRow(pageStatus, T("prog.status"), ref y);
        _lblSize = InfoRow(pageStatus, T("prog.size"), ref y);
        _lblDownloaded = InfoRow(pageStatus, T("prog.downloaded"), ref y);
        _lblRate = InfoRow(pageStatus, T("prog.rate"), ref y);
        _lblEta = InfoRow(pageStatus, T("prog.eta"), ref y);
        _lblResume = InfoRow(pageStatus, T("prog.resume"), ref y);

        _bar = new ProgressBar
        {
            Location = new Point(10, y + 4), Size = new Size(596, 20),
            Minimum = 0, Maximum = 1000,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        pageStatus.Controls.Add(_bar);
        y += 32;

        _btnHide = new Button
        {
            Text = T("prog.hide"), Location = new Point(10, y), Size = new Size(170, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnHide.Click += (_, _) => ToggleDetails();
        _btnPause = new Button
        {
            Text = T("prog.pause"), Location = new Point(330, y), Size = new Size(130, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnPause.Click += (_, _) => TogglePause();
        _btnCancel = new Button
        {
            Text = T("prog.cancel"), Location = new Point(476, y), Size = new Size(130, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnCancel.Click += (_, _) =>
        {
            var task = _manager.GetTask(_taskId);
            if (task != null) _manager.CancelDownload(task.Id);
        };
        pageStatus.Controls.Add(_btnHide);
        pageStatus.Controls.Add(_btnPause);
        pageStatus.Controls.Add(_btnCancel);
        y += 38;

        _lblDetailCaption = new Label
        {
            Text = T("prog.detailCaption"), Location = new Point(10, y), AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        pageStatus.Controls.Add(_lblDetailCaption);
        y += 20;

        _segBar = new Panel
        {
            Location = new Point(10, y), Size = new Size(596, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _segBar.Paint += SegBar_Paint;
        pageStatus.Controls.Add(_segBar);
        y += 30;

        _grid = new DataGridView
        {
            Location = new Point(10, y), Size = new Size(596, 150),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Theme.GridBack,
            GridColor = Theme.Border,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        var colN = new DataGridViewTextBoxColumn { Name = "N", HeaderText = T("prog.colN"), Width = 45 };
        var colDl = new DataGridViewTextBoxColumn { Name = "Dl", HeaderText = T("prog.colDownloaded"), Width = 140 };
        var colInfo = new DataGridViewTextBoxColumn { Name = "Info", HeaderText = T("prog.colInfo"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 120 };
        _grid.Columns.Add(colN);
        _grid.Columns.Add(colDl);
        _grid.Columns.Add(colInfo);
        pageStatus.Controls.Add(_grid);

        // ---- speed page ----
        var lblLimit = new Label
        {
            Text = T("prog.limitLabel"), AutoSize = true, Location = new Point(14, 18)
        };
        _nudLimit = new NumericUpDown
        {
            Location = new Point(14, 42), Size = new Size(300, 23),
            Minimum = 0, Maximum = 1000000, Increment = 100
        };
        var lblLimitHint = new Label
        {
            Text = T("sl.hint"), AutoSize = true, Location = new Point(14, 70)
        };
        var btnApplyLimit = new Button
        {
            Text = T("prog.apply"), Location = new Point(14, 100), Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat
        };
        btnApplyLimit.Click += (_, _) =>
        {
            if (_manager.SetTaskSpeedLimit(_taskId, (long)_nudLimit.Value * 1024))
            {
                _manager.SaveHistory(AppSettings.HistoryFile);
            }
        };
        _lblLimitCurrent = new Label { AutoSize = true, Location = new Point(14, 142) };
        pageSpeed.Controls.Add(lblLimit);
        pageSpeed.Controls.Add(_nudLimit);
        pageSpeed.Controls.Add(lblLimitHint);
        pageSpeed.Controls.Add(btnApplyLimit);
        pageSpeed.Controls.Add(_lblLimitCurrent);

        // ---- options page ----
        _chkCloseDone = new CheckBox
        {
            Text = T("prog.closeOnDone"), AutoSize = true, Location = new Point(14, 18)
        };
        _chkOpenFolderDone = new CheckBox
        {
            Text = T("prog.openFolderOnDone"), AutoSize = true, Location = new Point(14, 46)
        };
        _chkOpenFolderDone.CheckedChanged += (_, _) =>
        {
            var task = _manager.GetTask(_taskId);
            if (task != null && task.OpenFolderOnDone != _chkOpenFolderDone.Checked)
            {
                task.OpenFolderOnDone = _chkOpenFolderDone.Checked;
                _manager.SaveHistory(AppSettings.HistoryFile);
            }
        };
        pageOptions.Controls.Add(_chkCloseDone);
        pageOptions.Controls.Add(_chkOpenFolderDone);

        _expandedHeight = ClientSize.Height;

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RefreshValues();
        RefreshValues();
        _timer.Start();
        FormClosing += (_, _) => _timer.Stop();

        Theme.StyleForm(this);
    }

    private static Label InfoRow(Control parent, string caption, ref int y)
    {
        var lbl = new Label { Text = caption, Location = new Point(10, y), Size = new Size(140, 17), AutoSize = false };
        var value = new Label
        {
            Location = new Point(156, y), Size = new Size(450, 17), AutoSize = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        parent.Controls.Add(lbl);
        parent.Controls.Add(value);
        y += 23;
        return value;
    }

    private void ToggleDetails()
    {
        _detailsVisible = !_detailsVisible;
        _btnHide.Text = _detailsVisible ? T("prog.hide") : T("prog.show");
        _lblDetailCaption.Visible = _detailsVisible;
        _segBar.Visible = _detailsVisible;
        _grid.Visible = _detailsVisible;
        MinimumSize = new Size(560, _detailsVisible ? 420 : 320);
        ClientSize = new Size(ClientSize.Width, _detailsVisible ? _expandedHeight : _expandedHeight - 210);
    }

    private void TogglePause()
    {
        var task = _manager.GetTask(_taskId);
        if (task == null) return;
        if (task.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            _manager.PauseDownload(task.Id);
        }
        else if (task.Status is DownloadStatus.Paused or DownloadStatus.Error)
        {
            _manager.ResumeDownload(task.Id);
        }
    }

    private void RefreshValues()
    {
        var task = _manager.GetTask(_taskId);
        if (task == null)
        {
            Close();
            return;
        }

        Text = $"{task.ProgressPercentage:0}% {task.FileName}";
        if (_txtUrl.Text != task.Url) _txtUrl.Text = task.Url;
        _lblStatus.Text = task.StatusText;
        // Stream tasks carry a 0-100% placeholder on FileSize, never bytes:
        // show the percent itself and the neutral placeholders instead of
        // leaking the fake "25 B of 100 B" and a meaningless "0 B/s / 0 B/s".
        _lblSize.Text = task.SizeText;
        _lblDownloaded.Text = task.IsPercentProgress
            ? task.ProgressPercentage.ToString("0.0") + "%"
            : task.FileSize > 0
                ? string.Format(T("pr.of"), task.FormattedDownloadedBytes, task.FormattedFileSize, task.ProgressPercentage)
                : task.FormattedDownloadedBytes;
        _lblRate.Text = task.Type == DownloadType.Torrent
            ? $"{task.FormattedDownloadSpeed} / {task.FormattedUploadSpeed}"
            : task.FormattedSpeedDisplay;
        _lblEta.Text = task.FormattedTimeRemaining;
        _lblResume.Text = task.IsPercentProgress ? "—" : task.SupportsRange ? T("pr.yes") : T("pr.no");
        _bar.Value = (int)Math.Clamp(Math.Round(task.ProgressPercentage * 10), 0, 1000);

        _btnPause.Text = task.Status is DownloadStatus.Downloading or DownloadStatus.Queued
            ? T("prog.pause") : T("prog.resume");
        _btnPause.Enabled = task.Status is DownloadStatus.Downloading or DownloadStatus.Queued
            or DownloadStatus.Paused or DownloadStatus.Error;
        _btnCancel.Enabled = task.Status is DownloadStatus.Downloading or DownloadStatus.Queued
            or DownloadStatus.Paused;

        _nudLimit.Value = Math.Min(_nudLimit.Maximum, Math.Max(0, task.SpeedLimitBps / 1024));
        _lblLimitCurrent.Text = string.Format(T("prog.limitCurrent"), task.SpeedLimitText);
        if (_chkOpenFolderDone.Checked != task.OpenFolderOnDone)
        {
            _chkOpenFolderDone.Checked = task.OpenFolderOnDone;
        }
        _chkOpenFolderDone.Checked = task.OpenFolderOnDone;

        RefreshSegments(task);
        _segBar.Invalidate();

        if (task.Status == DownloadStatus.Completed && !_doneActionsFired)
        {
            _doneActionsFired = true;
            if (task.OpenFolderOnDone) OpenFolder(task.SavePath);
            if (_chkCloseDone.Checked) Close();
        }
    }

    private void RefreshSegments(DownloadTask task)
    {
        var stats = _manager.GetConnectionStats(task.Id);
        while (_grid.Rows.Count < stats.Count)
        {
            _grid.Rows.Add();
        }
        while (_grid.Rows.Count > stats.Count)
        {
            _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
        }
        for (var i = 0; i < stats.Count; i++)
        {
            var s = stats[i];
            var row = _grid.Rows[i];
            row.Cells[0].Value = s.Index + 1;
            row.Cells[1].Value = s.Length > 0
                ? $"{FormatBytes(s.Downloaded)} / {FormatBytes(s.Length)}"
                : FormatBytes(s.Downloaded);
            row.Cells[2].Value = InfoText(task, s);
        }
    }

    private string InfoText(DownloadTask task, SegmentStat s)
    {
        if (task.Type == DownloadType.Torrent && s.Active)
        {
            return string.Format(T("prog.torrentConn"), task.Connections);
        }
        if (s.Done) return T("prog.done");
        if (s.Active) return T("prog.receiving");
        return T("prog.waiting");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private void SegBar_Paint(object? sender, PaintEventArgs e)
    {
        var task = _manager.GetTask(_taskId);
        var g = e.Graphics;
        g.Clear(Theme.ProgressTrack);
        if (task == null || task.FileSize <= 0) return;

        var stats = _manager.GetConnectionStats(task.Id);
        var w = (float)_segBar.ClientSize.Width;
        var h = (float)_segBar.ClientSize.Height;
        foreach (var s in stats)
        {
            if (s.Length <= 0) continue;
            var x = (float)(s.Start / (double)task.FileSize * w);
            var bw = Math.Max(2f, (float)(s.Length / (double)task.FileSize * w));
            var frac = Math.Clamp(s.Downloaded / (double)s.Length, 0, 1);
            using var doneBrush = new SolidBrush(Theme.ProgressFill);
            using var activeBrush = new SolidBrush(Theme.AccentSoft);
            if (frac > 0)
            {
                g.FillRectangle(s.Done ? doneBrush : activeBrush, x, 0, (float)(bw * frac), h);
            }
        }
        using var pen = new Pen(Theme.Border);
        g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

}
