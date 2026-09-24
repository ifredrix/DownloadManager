using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Tor connection setup: Off, use an already-running Tor (Tor Browser or
/// expert bundle), or launch tor.exe automatically — preferably the copy
/// shipped inside Tor Browser, so nothing extra is downloaded.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class TorSettingsForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly AppSettings _settings;
    private readonly ComboBox _cmbMode;
    private readonly TextBox _txtEndpoint;
    private readonly TextBox _txtExe;
    private readonly CheckBox _chkRouteAll;
    private readonly Label _lblBundle;
    private readonly Button _btnBundle;
    private readonly Label _lblStatus;
    private readonly Button _btnDetect;
    private readonly Button _btnBrowse;
    private readonly Button _btnTest;
    private readonly Button _ok;
    private readonly Button _cancel;

    public TorSettingsForm(AppSettings settings, Action? onBundleInstalled = null)
    {
        _settings = settings;
        _onBundleInstalled = onBundleInstalled;
        _installedWhileManaged = string.Equals(
            settings.TorMode, "Managed", StringComparison.OrdinalIgnoreCase);

        Text = T("tor.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 294);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lblMode = new Label { Text = T("tor.mode"), AutoSize = true, Location = new Point(16, 18) };
        _cmbMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(200, 15),
            Size = new Size(324, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _cmbMode.Items.AddRange(new object[]
        {
            T("tor.off"),
            T("tor.external"),
            T("tor.managed")
        });

        var lblEndpoint = new Label { Text = T("tor.endpoint"), AutoSize = true, Location = new Point(16, 52) };
        _txtEndpoint = new TextBox
        {
            Location = new Point(200, 49),
            Size = new Size(324, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var lblExe = new Label { Text = T("tor.exe"), AutoSize = true, Location = new Point(16, 86) };
        _txtExe = new TextBox
        {
            Location = new Point(200, 83),
            Size = new Size(190, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _btnDetect = new Button { Text = T("tor.find"), Location = new Point(394, 82), Size = new Size(48, 25), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _btnDetect.Click += (_, _) => DetectExe();
        _btnBrowse = new Button { Text = "...", Location = new Point(446, 82), Size = new Size(36, 25), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _btnBrowse.Click += (_, _) => BrowseExe();

        _chkRouteAll = new CheckBox
        {
            Text = T("tor.routeAll"),
            AutoSize = true,
            Location = new Point(16, 118)
        };

        // Fixed-width rows: tor.hint spans two lines (\r\n) and no button
        // ever shares a row with a label, so long localized strings and
        // DPI scaling can never stack controls on top of each other.
        var hint = new Label
        {
            Text = T("tor.hint"),
            AutoSize = false,
            Location = new Point(16, 146),
            Size = new Size(508, 36)
        };

        _lblBundle = new Label { Text = "Bundled Tor: checking...", AutoSize = false, Location = new Point(16, 186), Size = new Size(508, 20) };

        _lblStatus = new Label { Text = "Status: -", AutoSize = false, Location = new Point(16, 210), Size = new Size(508, 36) };

        _btnTest = new Button { Text = T("tor.test"), Location = new Point(16, 252), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _btnTest.Click += async (_, _) => await TestAsync();
        _btnBundle = new Button { Text = T("tor.checkUpdate"), Location = new Point(196, 252), Size = new Size(150, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _btnBundle.Click += async (_, _) => await BundleFlowAsync();

        _ok = new Button { Text = T("dlg.ok"), Location = new Point(342, 252), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };
        _cancel = new Button { Text = T("dlg.cancel"), Location = new Point(438, 252), Size = new Size(86, 30), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(lblMode);
        Controls.Add(_cmbMode);
        Controls.Add(lblEndpoint);
        Controls.Add(_txtEndpoint);
        Controls.Add(lblExe);
        Controls.Add(_txtExe);
        Controls.Add(_btnDetect);
        Controls.Add(_btnBrowse);
        Controls.Add(_chkRouteAll);
        Controls.Add(hint);
        Controls.Add(_lblBundle);
        Controls.Add(_btnBundle);
        Controls.Add(_lblStatus);
        Controls.Add(_btnTest);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        LoadValues();
        _ok.Tag = "primary";
        Theme.StyleForm(this);
        _ = RefreshBundleLabelAsync();
    }

    private readonly Action? _onBundleInstalled;
    private readonly bool _installedWhileManaged;

    private TorBundle.ReleaseInfo? _pendingRelease;

    private async System.Threading.Tasks.Task RefreshBundleLabelAsync()
    {
        try
        {
            string? local = null;
            if (File.Exists(TorBundle.BundledExePath))
            {
                local = await TorBundle.LocalVersionAsync(TorBundle.BundledExePath);
            }
            _lblBundle.Text = local == null
                ? T("tor.bundledNone")
                : $"Bundled Tor: v{local}";
        }
        catch
        {
            _lblBundle.Text = T("tor.bundledUnknown");
        }
    }

    private async System.Threading.Tasks.Task BundleFlowAsync()
    {
        // Second press with a pending release starts the install.
        if (_pendingRelease != null)
        {
            await InstallBundleAsync(_pendingRelease);
            _pendingRelease = null;
            _btnBundle.Text = T("tor.checkUpdate");
            return;
        }

        _btnBundle.Enabled = false;
        _lblStatus.Text = T("tor.checking");
        try
        {
            var release = await TorBundle.CheckLatestAsync();
            string? local = null;
            if (File.Exists(TorBundle.BundledExePath))
            {
                local = await TorBundle.LocalVersionAsync(TorBundle.BundledExePath);
            }

            var latest = string.IsNullOrEmpty(release.TorVersion) ? release.BrowserVersion : release.TorVersion;
            if (local != null && string.Equals(local, release.TorVersion, StringComparison.OrdinalIgnoreCase))
            {
                _lblStatus.Text = string.Format(T("tor.noUpdate"), local);
                return;
            }

            _pendingRelease = release;
            _btnBundle.Text = string.Format(T("tor.download"), latest);
            _lblStatus.Text = local == null
                ? string.Format(T("tor.updateAvailNew"), latest)
                : string.Format(T("tor.updateAvail"), local, latest);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Status: " + ex.Message;
        }
        finally
        {
            _btnBundle.Enabled = true;
        }
    }

    private async System.Threading.Tasks.Task InstallBundleAsync(TorBundle.ReleaseInfo release)
    {
        _btnBundle.Enabled = false;
        try
        {
            var progress = new Progress<double>(pct => _lblStatus.Text = string.Format(T("tor.downloading"), pct));
            var exe = await TorBundle.InstallAsync(release, progress);
            _txtExe.Text = exe;
            // Adopt the fresh bundle immediately and persist it: the old
            // path must not survive in settings just because the user
            // later presses Batal instead of OK.
            _settings.TorExePath = exe;
            _settings.Save();
            _lblStatus.Text = string.Format(T("tor.installed"), exe);
            if (_cmbMode.SelectedIndex != 2)
            {
                _lblStatus.Text += " " + T("tor.externalKeepsRunning");
            }
            await RefreshBundleLabelAsync();
            // Managed Tor already active: restart it from the new binary
            // now, so "downloaded" really means "installed and running".
            // (A mode just switched to Managed inside this dialog is
            // covered by OK -> ApplyTorMode instead, avoiding an orphan
            // process when the dialog ends with Batal.)
            if (_installedWhileManaged && _cmbMode.SelectedIndex == 2)
            {
                _onBundleInstalled?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Status: " + ex.Message;
        }
        finally
        {
            _btnBundle.Enabled = true;
        }
    }

    private void LoadValues()
    {
        _cmbMode.SelectedIndex = NormalizeMode(_settings.TorMode) switch
        {
            "External" => 1,
            "Managed" => 2,
            _ => 0
        };
        _txtEndpoint.Text = $"{_settings.TorHost}:{_settings.TorPort}";
        _txtExe.Text = _settings.TorExePath;
        _chkRouteAll.Checked = _settings.TorRouteAll;
    }

    private static string NormalizeMode(string value)
    {
        if (string.Equals(value, "External", StringComparison.OrdinalIgnoreCase)) return "External";
        if (string.Equals(value, "Managed", StringComparison.OrdinalIgnoreCase)) return "Managed";
        return "Off";
    }

    private void Save()
    {
        _settings.TorMode = _cmbMode.SelectedIndex switch { 1 => "External", 2 => "Managed", _ => "Off" };
        _settings.TorRouteAll = _chkRouteAll.Checked;
        _settings.TorExePath = _txtExe.Text.Trim();
        ParseEndpoint(_txtEndpoint.Text.Trim(), out var host, out var port);
        _settings.TorHost = host;
        _settings.TorPort = port;
    }

    private static void ParseEndpoint(string text, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 9050;
        if (string.IsNullOrWhiteSpace(text)) return;
        var parts = text.Split(':');
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) &&
            int.TryParse(parts[1], out var parsed) && parsed > 0 && parsed < 65536)
        {
            host = parts[0].Trim();
            port = parsed;
        }
    }

    private void DetectExe()
    {
        var found = TorManager.FindTorExe(_txtExe.Text.Trim());
        if (found == null)
        {
            _lblStatus.Text = T("tor.notFound");
            return;
        }
        _txtExe.Text = found;
        _lblStatus.Text = T("tor.exeFound");
    }

    private void BrowseExe()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "tor.exe|tor.exe|All files (*.*)|*.*",
            Title = T("tor.exe")
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _txtExe.Text = dialog.FileName;
        }
    }

    private async System.Threading.Tasks.Task TestAsync()
    {
        _btnTest.Enabled = false;
        try
        {
            if (_cmbMode.SelectedIndex == 2)
            {
                var exe = TorManager.FindTorExe(_txtExe.Text.Trim());
                _lblStatus.Text = exe == null
                    ? "Status: tor.exe not found."
                    : $"Status: tor.exe OK ({exe}).";
                return;
            }

            ParseEndpoint(_txtEndpoint.Text.Trim(), out var host, out var port);
            var ok = await TorProxy.IsAvailableAsync(host, port);
            _lblStatus.Text = ok
                ? string.Format(T("tor.reachable"), host, port)
                : string.Format(T("tor.unreachable"), host, port);
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }
}
