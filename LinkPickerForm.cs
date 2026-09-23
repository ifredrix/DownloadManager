using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Small "Grab links" picker. Built in code (no designer file) because it is a
/// throw-away dialog with a generated control list.
/// </summary>
public sealed class LinkPickerForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly CheckedListBox _list;
    private readonly Button _ok;
    private readonly Button _cancel;
    private readonly Button _selectAll;

    private LinkPickerForm(IReadOnlyList<string> links)
    {
        Text = T("grab.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 300);
        ClientSize = new Size(720, 340);
        BackColor = Theme.WindowBack;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = false;

        var hint = new Label
        {
            Text = T("grab.hint"),
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Location = new Point(12, 12)
        };

        _list = new CheckedListBox
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true,
            ForeColor = Theme.Text,
            HorizontalScrollbar = true,
            IntegralHeight = false,
            Location = new Point(12, 36),
            Size = new Size(696, 240)
        };

        foreach (var link in links)
        {
            _list.Items.Add(link, CheckState.Checked);
        }

        _selectAll = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text,
            Location = new Point(12, 292),
            Size = new Size(110, 30),
            Text = T("grab.selectNone")
        };
        _selectAll.FlatAppearance.BorderColor = Theme.Border;
        _selectAll.Click += SelectAll_Click;

        _ok = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            BackColor = Theme.Accent,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Location = new Point(528, 292),
            Size = new Size(90, 30),
            Text = T("grab.download")
        };
        _ok.FlatAppearance.BorderSize = 0;
        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        _cancel = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text,
            Location = new Point(624, 292),
            Size = new Size(84, 30),
            Text = T("dlg.cancel")
        };
        _cancel.FlatAppearance.BorderColor = Theme.Border;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(_selectAll);
        Controls.Add(_ok);
        Controls.Add(_cancel);
    }

    private void SelectAll_Click(object? sender, EventArgs e)
    {
        var target = _list.CheckedItems.Count == 0;
        for (var i = 0; i < _list.Items.Count; i++)
        {
            _list.SetItemChecked(i, target);
        }
        _selectAll.Text = target ? T("grab.selectNone") : T("grab.selectAll");
    }

    /// <summary>Shows the picker and returns the links the user ticked.</summary>
    public static List<string> Show(IWin32Window owner, IReadOnlyList<string> links)
    {
        using var dialog = new LinkPickerForm(links);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return new List<string>();

        var picked = new List<string>();
        foreach (var item in dialog._list.CheckedItems)
        {
            if (item is string link) picked.Add(link);
        }
        return picked;
    }
}
