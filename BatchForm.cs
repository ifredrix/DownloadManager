using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Paste-a-list batch add: one URL per line, each routed like a normal add.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class BatchForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly TextBox _txtList;
    private readonly Label _lblStatus;
    private readonly Button _btnAdd;
    private readonly Button _btnClose;

    public List<string> Links { get; } = new();

    public BatchForm()
    {
        Text = T("batch.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 360);
        ClientSize = new Size(560, 420);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("batch.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _txtList = new TextBox
        {
            Location = new Point(12, 36),
            Size = new Size(536, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F)
        };

        _lblStatus = new Label
        {
            Text = string.Empty,
            AutoSize = true,
            Location = new Point(12, 326),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        _btnAdd = new Button
        {
            Text = T("batch.add"),
            Location = new Point(360, 376),
            Size = new Size(94, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnAdd.Click += (_, _) => Collect();

        _btnClose = new Button
        {
            Text = T("dlg.close"),
            Location = new Point(460, 376),
            Size = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnClose.Click += (_, _) => Close();

        AcceptButton = _btnAdd;
        CancelButton = _btnClose;

        Controls.Add(hint);
        Controls.Add(_txtList);
        Controls.Add(_lblStatus);
        Controls.Add(_btnAdd);
        Controls.Add(_btnClose);

        _btnAdd.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void Collect()
    {
        Links.Clear();
        foreach (var raw in _txtList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim().Trim('"');
            if (string.IsNullOrEmpty(line)) continue;
            Links.AddRange(MainForm.ExtractLinks(line));
            if (MainForm.IsLocalTorrentFile(line)) Links.Add(line);
        }

        var distinct = Links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Links.Clear();
        Links.AddRange(distinct);

        if (Links.Count == 0)
        {
            _lblStatus.Text = string.Format(T("batch.found"), 0);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
