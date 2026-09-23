using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Saved per-site logins: host -&gt; Basic
/// credentials, applied automatically to HTTP and FTP downloads.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class SiteLoginsForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly AppSettings _settings;
    private readonly ListView _list;
    private readonly TextBox _txtHost;
    private readonly TextBox _txtUser;
    private readonly TextBox _txtPass;
    private readonly Button _btnAdd;
    private readonly Button _btnDelete;
    private readonly Button _ok;
    private readonly Button _cancel;

    public SiteLoginsForm(AppSettings settings)
    {
        _settings = settings;

        Text = T("login.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 380);
        ClientSize = new Size(560, 420);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("login.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _list = new ListView
        {
            Location = new Point(12, 36),
            Size = new Size(536, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _list.Columns.Add(T("login.host"), 250);
        _list.Columns.Add(T("login.user"), 270);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        var lblHost = new Label { Text = T("login.host"), AutoSize = true, Location = new Point(12, 248), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _txtHost = new TextBox { Location = new Point(12, 268), Size = new Size(250, 23), BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        var lblUser = new Label { Text = T("login.user"), AutoSize = true, Location = new Point(274, 248), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _txtUser = new TextBox { Location = new Point(274, 268), Size = new Size(130, 23), BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var lblPass = new Label { Text = T("login.pass"), AutoSize = true, Location = new Point(412, 248), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _txtPass = new TextBox { Location = new Point(412, 268), Size = new Size(136, 23), BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

        _btnAdd = new Button { Text = T("login.addUpdate"), Location = new Point(12, 300), Size = new Size(110, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _btnAdd.Click += (_, _) => AddOrUpdate();
        _btnDelete = new Button { Text = T("btn.remove"), Location = new Point(128, 300), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _btnDelete.Click += (_, _) => DeleteSelected();

        _ok = new Button { Text = T("dlg.ok"), Location = new Point(362, 378), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };
        _cancel = new Button { Text = T("dlg.cancel"), Location = new Point(458, 378), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(hint);
        Controls.Add(_list);
        Controls.Add(lblHost);
        Controls.Add(_txtHost);
        Controls.Add(lblUser);
        Controls.Add(_txtUser);
        Controls.Add(lblPass);
        Controls.Add(_txtPass);
        Controls.Add(_btnAdd);
        Controls.Add(_btnDelete);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        Reload();
        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private Dictionary<string, SiteLogin> _pending = new(StringComparer.OrdinalIgnoreCase);

    private void Reload()
    {
        _pending = new Dictionary<string, SiteLogin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _settings.SiteLogins)
        {
            _pending[pair.Key] = new SiteLogin { User = pair.Value.User, Pass = pair.Value.Pass };
        }
        _list.Items.Clear();
        foreach (var pair in _pending.OrderBy(p => p.Key))
        {
            var item = new ListViewItem(pair.Key);
            item.SubItems.Add(pair.Value.User);
            _list.Items.Add(item);
        }
    }

    private void ShowSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var host = _list.SelectedItems[0].Text;
        if (!_pending.TryGetValue(host, out var login)) return;
        _txtHost.Text = host;
        _txtUser.Text = login.User;
        _txtPass.Text = login.Pass;
    }

    private void AddOrUpdate()
    {
        var host = _txtHost.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(_txtUser.Text.Trim()))
        {
            MessageBox.Show(this, T("login.needBoth"), T("login.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _pending[host] = new SiteLogin { User = _txtUser.Text.Trim(), Pass = _txtPass.Text };
        Reload();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        _pending.Remove(_list.SelectedItems[0].Text);
        Reload();
    }

    private void Save()
    {
        _settings.SiteLogins = new Dictionary<string, SiteLogin>(_pending, StringComparer.OrdinalIgnoreCase);
    }
}
