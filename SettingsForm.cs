using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

public partial class SettingsForm : Form
{
    private static string T(string key) => Localization.T(key);

    private static readonly string[] AfterDoneKeys = { "None", "Shutdown", "Sleep", "Hibernate" };

    private readonly AppSettings _settings;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadValues();
        ApplyTexts();
        btnOK.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void ApplyTexts()
    {
        Text = T("set.title");
        lblDownloadPath.Text = T("set.folder");
        btnBrowse.Text = T("dlg.browse");
        lblMaxConcurrent.Text = T("set.concurrent");
        lblConnections.Text = T("set.connections");
        lblSpeedLimit.Text = T("set.speed");
        lblSpeedHint.Text = T("set.speedHint");
        chkClipboard.Text = T("set.clipboard");
        chkAutoStart.Text = T("set.autostartDl");
        chkNotifications.Text = T("set.notify");
        chkScheduleStart.Text = T("set.schedStart");
        chkScheduleStop.Text = T("set.schedStop");
        lblAfterDone.Text = T("set.afterDone");
        lblTheme.Text = T("set.skin");
        lblAccent.Text = T("set.accent");
        btnCustomizeToolbar.Text = T("set.customToolbar");
        chkTorAll.Text = T("set.routeTor");
        btnTorSetup.Text = T("set.torSetup");
        chkStartWindows.Text = T("set.startWindows");
        chkConfirmDl.Text = T("set.confirmDl");
        btnOK.Text = T("dlg.save");
        btnCancel.Text = T("dlg.cancel");

        var afterDone = cmbAfterDone.SelectedIndex;
        cmbAfterDone.Items.Clear();
        cmbAfterDone.Items.AddRange(new object[]
            { T("set.none"), T("set.shutdown"), T("set.sleep"), T("set.hibernate") });
        cmbAfterDone.SelectedIndex = afterDone >= 0 ? afterDone : 0;
    }

    private void LoadValues()
    {
        txtDownloadPath.Text = _settings.DownloadPath;
        nudMaxConcurrent.Value = Math.Min(nudMaxConcurrent.Maximum,
            Math.Max(nudMaxConcurrent.Minimum, _settings.MaxConcurrentDownloads));
        nudConnections.Value = Math.Min(nudConnections.Maximum,
            Math.Max(nudConnections.Minimum, _settings.ConnectionsPerDownload));
        nudSpeedLimit.Value = Math.Min(nudSpeedLimit.Maximum,
            Math.Max(0, _settings.SpeedLimitBytesPerSecond / 1024));

        chkClipboard.Checked = _settings.MonitorClipboard;
        chkAutoStart.Checked = _settings.AutoStartDownloads;
        chkNotifications.Checked = _settings.ShowNotifications;

        chkScheduleStart.Checked = _settings.ScheduleStartEnabled;
        dtpScheduleStart.Value = ParseTime(_settings.ScheduleStartTime);
        chkScheduleStop.Checked = _settings.ScheduleStopEnabled;
        dtpScheduleStop.Value = ParseTime(_settings.ScheduleStopTime);
        cmbAfterDone.SelectedIndex = AfterDoneIndex(_settings.ActionAfterAllDone);
        cmbTheme.SelectedItem = NormalizeTheme(_settings.ThemeMode);
        cmbAccent.SelectedItem = NormalizeAccent(_settings.AccentName);
        chkTorAll.Checked = _settings.TorRouteAll;
        chkConfirmDl.Checked = _settings.ConfirmNewDownload;
        chkStartWindows.Checked = AutoStart.IsEnabled();
        cmbTheme.SelectedIndexChanged += (_, _) => PreviewSkin();
        cmbAccent.SelectedIndexChanged += (_, _) => PreviewSkin();
    }

    private static int AfterDoneIndex(string value)
    {
        for (var i = 0; i < AfterDoneKeys.Length; i++)
        {
            if (string.Equals(AfterDoneKeys[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return 0;
    }

    private static string NormalizeTheme(string value) =>
        string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";

    private static string NormalizeAccent(string value)
    {
        foreach (var name in Theme.AccentNames)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) return name;
        }
        return "Blue";
    }

    /// <summary>Live skin preview inside the settings dialog.</summary>
    private void PreviewSkin()
    {
        Theme.Apply(cmbTheme.SelectedItem?.ToString(), cmbAccent.SelectedItem?.ToString());
        Theme.StyleForm(this);
    }

    private void btnTorSetup_Click(object? sender, EventArgs e)
    {
        using var dialog = new TorSettingsForm(_settings);
        dialog.ShowDialog(this);
    }

    private void btnCustomizeToolbar_Click(object? sender, EventArgs e)
    {
        var order = new List<string>(_settings.ToolbarOrder);
        var hidden = new HashSet<string>(_settings.ToolbarHidden ?? new(), StringComparer.OrdinalIgnoreCase);
        using var dialog = new ToolbarEditorForm(order, hidden);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.ToolbarOrder = dialog.Order;
            _settings.ToolbarHidden = dialog.Hidden.ToList();
        }
    }

    private static DateTime ParseTime(string value)
    {
        if (TimeSpan.TryParse(value, out var ts))
        {
            return DateTime.Today.Add(ts);
        }
        return DateTime.Today.AddHours(2);
    }

    private void btnBrowse_Click(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T("set.folder"),
            SelectedPath = Directory.Exists(txtDownloadPath.Text)
                ? txtDownloadPath.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtDownloadPath.Text = dialog.SelectedPath;
        }
    }

    private void btnOK_Click(object sender, EventArgs e)
    {
        var path = txtDownloadPath.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show(this, T("msg.noFolder"), T("msg.settingsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(T("msg.badFolder"), ex.Message), T("msg.settingsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _settings.DownloadPath = path;
        _settings.MaxConcurrentDownloads = (int)nudMaxConcurrent.Value;
        _settings.ConnectionsPerDownload = (int)nudConnections.Value;
        _settings.SpeedLimitBytesPerSecond = (long)nudSpeedLimit.Value * 1024;
        _settings.MonitorClipboard = chkClipboard.Checked;
        _settings.AutoStartDownloads = chkAutoStart.Checked;
        _settings.ShowNotifications = chkNotifications.Checked;
        _settings.ScheduleStartEnabled = chkScheduleStart.Checked;
        _settings.ScheduleStartTime = dtpScheduleStart.Value.ToString("HH:mm");
        _settings.ScheduleStopEnabled = chkScheduleStop.Checked;
        _settings.ScheduleStopTime = dtpScheduleStop.Value.ToString("HH:mm");
        var afterDone = cmbAfterDone.SelectedIndex;
        _settings.ActionAfterAllDone = afterDone >= 0 && afterDone < AfterDoneKeys.Length
            ? AfterDoneKeys[afterDone]
            : "None";
        _settings.ThemeMode = cmbTheme.SelectedItem?.ToString() ?? "Light";
        _settings.AccentName = cmbAccent.SelectedItem?.ToString() ?? "Blue";
        _settings.TorRouteAll = chkTorAll.Checked;
        _settings.ConfirmNewDownload = chkConfirmDl.Checked;
        _settings.AutoStart = chkStartWindows.Checked;
        try
        {
            AutoStart.SetEnabled(chkStartWindows.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(T("msg.startupFail"), ex.Message), T("msg.settingsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}

/// <summary>Windows logon autostart via HKCU...\Run (no admin needed).</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ifredrixDownloadManager";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Cannot open the startup registry key.");
        if (enabled)
        {
            key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --tray");
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName);
        }
    }
}
