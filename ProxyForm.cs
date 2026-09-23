using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// HTTP proxy setup. Site logins need
/// no UI: embed credentials in the URL (<c>http://user:pass@host/file</c>)
/// and they are sent as Basic auth. Cookies are kept per session
/// automatically. Built in code (no designer file).
/// </summary>
public sealed class ProxyForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly AppSettings _settings;
    private readonly ComboBox _cmbMode;
    private readonly TextBox _txtHost;
    private readonly NumericUpDown _nudPort;
    private readonly TextBox _txtUser;
    private readonly TextBox _txtPass;
    private readonly Label _lblStatus;
    private readonly Button _btnTest;
    private readonly Button _ok;
    private readonly Button _cancel;

    public ProxyForm(AppSettings settings)
    {
        _settings = settings;

        Text = T("proxy.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 300);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lblMode = new Label { Text = T("proxy.mode"), AutoSize = true, Location = new Point(16, 18) };
        _cmbMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(160, 15),
            Size = new Size(244, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _cmbMode.Items.AddRange(new object[] { "None", "System", "Custom" });

        var lblHost = new Label { Text = T("proxy.host"), AutoSize = true, Location = new Point(16, 52) };
        _txtHost = new TextBox
        {
            Location = new Point(160, 49),
            Size = new Size(160, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _nudPort = new NumericUpDown
        {
            Location = new Point(326, 49),
            Size = new Size(78, 23),
            Minimum = 1,
            Maximum = 65535,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        var lblUser = new Label { Text = T("proxy.user"), AutoSize = true, Location = new Point(16, 86) };
        _txtUser = new TextBox
        {
            Location = new Point(160, 83),
            Size = new Size(244, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var lblPass = new Label { Text = T("proxy.pass"), AutoSize = true, Location = new Point(16, 120) };
        _txtPass = new TextBox
        {
            Location = new Point(160, 117),
            Size = new Size(244, 23),
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var hint = new Label
        {
            Text = T("proxy.hint"),
            AutoSize = true,
            Location = new Point(16, 152)
        };

        _lblStatus = new Label { Text = "Status: -", AutoSize = true, Location = new Point(16, 200) };

        _btnTest = new Button { Text = T("proxy.test"), Location = new Point(16, 226), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _btnTest.Click += async (_, _) => await TestAsync();

        _ok = new Button { Text = T("dlg.ok"), Location = new Point(222, 226), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };
        _cancel = new Button { Text = T("dlg.cancel"), Location = new Point(318, 226), Size = new Size(86, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(lblMode);
        Controls.Add(_cmbMode);
        Controls.Add(lblHost);
        Controls.Add(_txtHost);
        Controls.Add(_nudPort);
        Controls.Add(lblUser);
        Controls.Add(_txtUser);
        Controls.Add(lblPass);
        Controls.Add(_txtPass);
        Controls.Add(hint);
        Controls.Add(_lblStatus);
        Controls.Add(_btnTest);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        LoadValues();
        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void LoadValues()
    {
        _cmbMode.SelectedItem = NormalizeMode(_settings.ProxyMode);
        _txtHost.Text = _settings.ProxyHost;
        _nudPort.Value = Math.Clamp(_settings.ProxyPort, 1, 65535);
        _txtUser.Text = _settings.ProxyUser;
        _txtPass.Text = _settings.ProxyPass;
    }

    private static string NormalizeMode(string value)
    {
        if (string.Equals(value, "System", StringComparison.OrdinalIgnoreCase)) return "System";
        if (string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase)) return "Custom";
        return "None";
    }

    private void Save()
    {
        _settings.ProxyMode = _cmbMode.SelectedItem?.ToString() ?? "None";
        _settings.ProxyHost = _txtHost.Text.Trim();
        _settings.ProxyPort = (int)_nudPort.Value;
        _settings.ProxyUser = _txtUser.Text;
        _settings.ProxyPass = _txtPass.Text;
    }

    private async System.Threading.Tasks.Task TestAsync()
    {
        _btnTest.Enabled = false;
        try
        {
            var mode = _cmbMode.SelectedItem?.ToString() ?? "None";
            if (!string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                _lblStatus.Text = string.Format(T("proxy.nothing"), mode);
                return;
            }

            using var tcp = new System.Net.Sockets.TcpClient();
            try
            {
                await tcp.ConnectAsync(_txtHost.Text.Trim(), (int)_nudPort.Value)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                _lblStatus.Text = T("proxy.reachable");
            }
            catch
            {
                _lblStatus.Text = T("proxy.unreachable");
            }
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }
}
