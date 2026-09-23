using System;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Per-download speed cap in KB/s (0 = use the global limit).
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class SpeedLimitForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly NumericUpDown _nud;
    private readonly Button _ok;
    private readonly Button _cancel;

    /// <summary>Chosen cap in bytes/s (0 = global).</summary>
    public long BytesPerSecond { get; private set; }

    public SpeedLimitForm(string fileName, long currentBytesPerSecond)
    {
        BytesPerSecond = Math.Max(0, currentBytesPerSecond);

        Text = T("sl.title") + " - " + fileName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 130);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lbl = new Label
        {
            Text = T("sl.limitTo"),
            AutoSize = true,
            Location = new Point(16, 18)
        };
        _nud = new NumericUpDown
        {
            Location = new Point(16, 42),
            Size = new Size(328, 23),
            Minimum = 0,
            Maximum = 1000000,
            Increment = 100,
            Value = Math.Min(1000000, Math.Max(0, currentBytesPerSecond / 1024)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var hint = new Label
        {
            Text = T("sl.hint"),
            AutoSize = true,
            Location = new Point(16, 70)
        };

        _ok = new Button
        {
            Text = T("dlg.ok"),
            Location = new Point(168, 92),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _ok.Click += (_, _) =>
        {
            BytesPerSecond = (long)_nud.Value * 1024;
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancel = new Button
        {
            Text = T("dlg.cancel"),
            Location = new Point(264, 92),
            Size = new Size(80, 28),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(lbl);
        Controls.Add(_nud);
        Controls.Add(hint);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }
}
