using System;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// URL exclusion editor: wildcard patterns ignored by automatic capture.
/// Built in code (no designer file) like the other small dialogs.
/// </summary>
public sealed class ExclusionsForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly AppSettings _settings;
    private readonly TextBox _txtPatterns;
    private readonly TextBox _txtTest;
    private readonly Label _lblResult;
    private readonly Button _ok;
    private readonly Button _cancel;

    public ExclusionsForm(AppSettings settings)
    {
        _settings = settings;

        Text = T("ex.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 380);
        ClientSize = new Size(560, 420);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("ex.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _txtPatterns = new TextBox
        {
            Location = new Point(12, 58),
            Size = new Size(536, 220),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F),
            Text = _settings.ExcludedPatterns
        };

        var lblTest = new Label
        {
            Text = T("ex.test"),
            AutoSize = true,
            Location = new Point(12, 290),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _txtTest = new TextBox
        {
            Location = new Point(100, 287),
            Size = new Size(348, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        var btnCheck = new Button
        {
            Text = T("ex.check"),
            Location = new Point(454, 286),
            Size = new Size(94, 25),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnCheck.Click += (_, _) => CheckTest();

        _lblResult = new Label
        {
            Text = string.Empty,
            AutoSize = true,
            Location = new Point(12, 316),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        _ok = new Button
        {
            Text = T("dlg.ok"),
            Location = new Point(366, 378),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; Close(); };

        _cancel = new Button
        {
            Text = T("dlg.cancel"),
            Location = new Point(462, 378),
            Size = new Size(86, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(hint);
        Controls.Add(_txtPatterns);
        Controls.Add(lblTest);
        Controls.Add(_txtTest);
        Controls.Add(btnCheck);
        Controls.Add(_lblResult);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private void CheckTest()
    {
        var url = _txtTest.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            _lblResult.Text = string.Empty;
            return;
        }
        var excluded = Exclusions.IsExcluded(url, _txtPatterns.Text);
        _lblResult.Text = excluded ? T("ex.excluded") : T("ex.allowed");
        _lblResult.ForeColor = excluded ? Theme.Danger : Theme.TextMuted;
    }

    private void Save()
    {
        _settings.ExcludedPatterns = _txtPatterns.Text;
    }
}
