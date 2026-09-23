using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Shows every detected browser with extension status and a guided install.
/// Extensions cannot be installed silently (browser security), so Install
/// opens the browser's extensions page, copies the extension folder path,
/// and shows the exact steps. Built in code (no designer file).
/// </summary>
public sealed class BrowserIntegrationForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly List<BrowserTarget> _targets;
    private readonly Func<IReadOnlyDictionary<string, DateTime>> _heartbeats;
    private readonly FlowLayoutPanel _rows;
    private readonly Button _close;

    public BrowserIntegrationForm(
        List<BrowserTarget> targets,
        Func<IReadOnlyDictionary<string, DateTime>> heartbeats)
    {
        _targets = targets;
        _heartbeats = heartbeats;

        Text = T("br.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 320);
        ClientSize = new Size(620, 380);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("br.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _rows = new FlowLayoutPanel
        {
            Location = new Point(12, 36),
            Size = new Size(596, 288),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        _close = new Button
        {
            Text = T("dlg.close"),
            Size = new Size(98, 30),
            Location = new Point(510, 336),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat
        };
        _close.Click += (_, _) => Close();

        Controls.Add(hint);
        Controls.Add(_rows);
        Controls.Add(_close);

        Reload();
        _close.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void Reload()
    {
        _rows.Controls.Clear();
        var beats = new Dictionary<string, DateTime>(_heartbeats());

        foreach (var target in _targets)
        {
            // Brave/Vivaldi reuse the Chrome extension folder, so they share
            // its heartbeat source.
            var source = target.Id is "brave" or "vivaldi" ? "chrome" : target.Id;
            var connected = beats.TryGetValue(source, out var t) &&
                            (DateTime.UtcNow - t.ToUniversalTime()).TotalSeconds < 30;

            var row = new Panel
            {
                Size = new Size(570, 64),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 8)
            };

            var name = new Label
            {
                Text = target.Display,
                Font = new Font("Segoe UI Semibold", 9F),
                Location = new Point(8, 6),
                AutoSize = true
            };
            var status = new Label
            {
                Text = (target.IsInstalled ? T("br.installed") : T("br.notFound")) +
                       "  •  extension " + (connected ? T("br.connected") : T("br.notSeen")),
                Location = new Point(8, 28),
                AutoSize = true
            };
            var install = new Button
            {
                Text = T("br.install"),
                Size = new Size(90, 28),
                Location = new Point(468, 16),
                FlatStyle = FlatStyle.Flat,
                Enabled = target.IsInstalled,
                Tag = target
            };
            install.Click += Install_Click;

            row.Controls.Add(name);
            row.Controls.Add(status);
            row.Controls.Add(install);
            _rows.Controls.Add(row);
        }
    }

    private void Install_Click(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: BrowserTarget target }) return;

        try { Clipboard.SetText(target.ExtensionDir); } catch { }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target.ExePath!,
                Arguments = target.Id == "firefox" ? target.ExtensionsPage : $"\"{target.ExtensionsPage}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(T("msg.browserFail"), target.Display, ex.Message),
                T("msg.browserFailTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var steps = target.Id == "firefox"
            ? string.Format(T("br.stepsFx"), target.ExtensionDir)
            : string.Format(T("br.stepsCr"), target.ExtensionDir);

        MessageBox.Show(this,
            string.Format(T("br.steps"), steps),
            "Install in " + target.Display,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
