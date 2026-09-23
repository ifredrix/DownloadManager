using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// "Grab from page" dialog: enter a page address, optionally crawl one level
/// of the same site, then pick links into the download queue.
/// Built in code (no designer file).
/// </summary>
public sealed class SpiderForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly TextBox _txtUrl;
    private readonly ComboBox _cmbDepth;
    private readonly CheckBox _chkFilesOnly;
    private readonly Button _btnFetch;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;
    private readonly TextBox _log;
    private CancellationTokenSource? _cts;

    public List<string> Found { get; private set; } = new();

    public SpiderForm(string initialUrl)
    {
        Text = T("spider.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 380);
        ClientSize = new Size(640, 420);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        _txtUrl = new TextBox
        {
            Location = new Point(12, 14), Width = 516, Height = 28,
            BorderStyle = BorderStyle.FixedSingle, Text = initialUrl,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _btnFetch = new Button
        {
            Text = T("spider.fetch"), Location = new Point(534, 13), Size = new Size(94, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat
        };
        _btnFetch.Click += async (_, _) => await FetchAsync();

        _cmbDepth = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(12, 52), Size = new Size(240, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _cmbDepth.Items.AddRange(new object[] { T("spider.depth0"), T("spider.depth1") });
        _cmbDepth.SelectedIndex = 0;

        _chkFilesOnly = new CheckBox
        {
            Text = T("spider.filesOnly"),
            Checked = true, AutoSize = true,
            Location = new Point(264, 55),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _lblStatus = new Label
        {
            Text = T("spider.idle"), Location = new Point(12, 84), AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _log = new TextBox
        {
            Location = new Point(12, 108), Size = new Size(616, 258),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F)
        };

        _btnClose = new Button
        {
            Text = T("dlg.close"), Location = new Point(534, 376), Size = new Size(94, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.Flat
        };
        _btnClose.Click += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            Close();
        };

        AcceptButton = _btnFetch;
        CancelButton = _btnClose;

        Controls.Add(_txtUrl);
        Controls.Add(_btnFetch);
        Controls.Add(_cmbDepth);
        Controls.Add(_chkFilesOnly);
        Controls.Add(_lblStatus);
        Controls.Add(_log);
        Controls.Add(_btnClose);

        FormClosing += (_, _) => { try { _cts?.Cancel(); } catch { } };

        _btnFetch.Tag = "primary";
        Theme.StyleForm(this);
    }

    private async Task FetchAsync()
    {
        var url = _txtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            _lblStatus.Text = T("spider.needUrl");
            return;
        }
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
            _txtUrl.Text = url;
        }

        _cts = new CancellationTokenSource();
        _btnFetch.Enabled = false;
        _log.Clear();
        Found = new List<string>();
        _lblStatus.Text = T("spider.fetching");

        try
        {
            var progress = new Progress<string>(line =>
            {
                _log.AppendText(line + Environment.NewLine);
            });
            Found = await SiteSpider.CrawlAsync(
                url, _cmbDepth.SelectedIndex, _chkFilesOnly.Checked, progress, _cts.Token);
            _lblStatus.Text = $"{Found.Count} link(s) found.";
            _log.AppendText($"--- {Found.Count} link(s) ---" + Environment.NewLine);
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = T("spider.cancelled");
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _btnFetch.Enabled = true;
        }
    }
}
