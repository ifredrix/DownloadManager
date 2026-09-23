using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Toolbar editor: tick buttons to show/hide, move them up/down.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class ToolbarEditorForm : Form
{
    private static string T(string key) => Localization.T(key);

    public static readonly IReadOnlyDictionary<string, string> ButtonTitles =
        new Dictionary<string, string>
        {
            ["AddUrl"] = Localization.T("btn.addUrl"),
            ["AddTorrent"] = Localization.T("btn.addTorrent"),
            ["Stream"] = Localization.T("btn.stream"),
            ["GrabLinks"] = Localization.T("btn.grabLinks"),
            ["Browsers"] = Localization.T("btn.browsers"),
            ["Settings"] = Localization.T("btn.settings"),
        };

    private readonly CheckedListBox _list;
    private readonly Button _up;
    private readonly Button _down;
    private readonly Button _reset;
    private readonly Button _ok;
    private readonly Button _cancel;

    public List<string> Order { get; private set; }
    public HashSet<string> Hidden { get; private set; }

    public ToolbarEditorForm(List<string> order, HashSet<string> hidden)
    {
        Order = order.ToList();
        Hidden = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);

        Text = T("tb.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 340);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("tb.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _list = new CheckedListBox
        {
            Location = new Point(12, 36),
            Size = new Size(240, 244),
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _up = new Button { Text = T("tb.up"), Location = new Point(258, 36), Size = new Size(90, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat };
        _up.Click += (_, _) => MoveItem(-1);
        _down = new Button { Text = T("tb.down"), Location = new Point(258, 72), Size = new Size(90, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat };
        _down.Click += (_, _) => MoveItem(1);
        _reset = new Button { Text = T("dlg.reset"), Location = new Point(258, 108), Size = new Size(90, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat };
        _reset.Click += (_, _) => Reset();

        _ok = new Button { Text = T("dlg.ok"), Location = new Point(168, 292), Size = new Size(90, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.Flat };
        _ok.Click += (_, _) => { Collect(); DialogResult = DialogResult.OK; Close(); };
        _cancel = new Button { Text = T("dlg.cancel"), Location = new Point(264, 292), Size = new Size(84, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.Flat };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(hint);
        Controls.Add(_list);
        Controls.Add(_up);
        Controls.Add(_down);
        Controls.Add(_reset);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        Reload();
        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var key in Order)
        {
            if (!ButtonTitles.TryGetValue(key, out var title)) continue;
            _list.Items.Add(key + "  —  " + title, !Hidden.Contains(key));
        }
    }

    private static string KeyOf(object? item)
    {
        var text = item?.ToString() ?? string.Empty;
        var sep = text.IndexOf("  —  ", StringComparison.Ordinal);
        return sep < 0 ? text : text[..sep];
    }

    private void Collect()
    {
        var order = new List<string>();
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _list.Items.Count; i++)
        {
            var key = KeyOf(_list.Items[i]);
            order.Add(key);
            if (_list.GetItemChecked(i) == false) hidden.Add(key);
        }
        Order = order;
        Hidden = hidden;
    }

    private void MoveItem(int delta)
    {
        var index = _list.SelectedIndex;
        if (index < 0) return;
        var other = index + delta;
        if (other < 0 || other >= _list.Items.Count) return;

        var item = _list.Items[index];
        var check = _list.GetItemChecked(index);
        var otherItem = _list.Items[other];
        var otherCheck = _list.GetItemChecked(other);

        _list.Items[index] = otherItem;
        _list.SetItemChecked(index, otherCheck);
        _list.Items[other] = item;
        _list.SetItemChecked(other, check);
        _list.SelectedIndex = other;
    }

    private void Reset()
    {
        Order = new List<string> { "AddUrl", "AddTorrent", "Stream", "GrabLinks", "Browsers", "Settings" };
        Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Reload();
    }
}
