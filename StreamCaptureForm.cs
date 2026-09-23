using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Modal dialog that lists yt-dlp formats and downloads the chosen one.
/// Code-built on purpose (no designer file) so the project tree stays lean.
/// </summary>
public sealed class StreamCaptureForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly DownloadManager _manager;
    private readonly string _initialUrl;
    private readonly TextBox _txtUrl;
    private readonly Button _btnList;
    private readonly Button _btnGetTool;
    private readonly Button _btnDownload;
    private readonly Button _btnClose;
    private readonly Button _btnOpenFolder;
    private readonly ListView _lvFormats;
    private readonly ProgressBar _progress;
    private readonly Label _lblProgress;
    private readonly Label _lblTool;
    private readonly TextBox _log;
    private CancellationTokenSource? _cts;
    private string? _outputDirectory;

    public StreamCaptureForm(DownloadManager manager, string initialUrl)
    {
        _manager = manager;
        _initialUrl = initialUrl;

        Text = T("stream.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(640, 480);
        ClientSize = new Size(820, 560);
        BackColor = Theme.WindowBack;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = false;

        _txtUrl = new TextBox
        {
            Location = new Point(12, 14), Width = 600, Height = 28,
            BorderStyle = BorderStyle.FixedSingle, Text = initialUrl,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _btnList = new Button
        {
            Text = T("stream.list"), Location = new Point(618, 13), Size = new Size(90, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.White,
            FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text
        };
        _btnList.FlatAppearance.BorderColor = Theme.Border;
        _btnList.Click += async (_, _) => await ListFormatsAsync();

        _btnGetTool = new Button
        {
            Text = T("stream.getTool"), Location = new Point(618, 13), Size = new Size(190, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Theme.Accent,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Visible = false
        };
        _btnGetTool.FlatAppearance.BorderSize = 0;
        _btnGetTool.Click += async (_, _) => await FetchToolAsync();

        _btnDownload = new Button
        {
            Text = T("stream.download"), Location = new Point(714, 13), Size = new Size(94, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Theme.Accent,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Enabled = false
        };
        _btnDownload.FlatAppearance.BorderSize = 0;
        _btnDownload.Click += async (_, _) => await DownloadAsync();

        _lblTool = new Label
        {
            Text = T("stream.detecting"), Location = new Point(12, 48),
            AutoSize = true, ForeColor = Theme.TextMuted, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _lvFormats = new ListView
        {
            Location = new Point(12, 72), Size = new Size(796, 300),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details, FullRowSelect = true, GridLines = false,
            MultiSelect = false, HideSelection = false,
            BackColor = Color.White, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
        };
        _lvFormats.Columns.Add("ID", 60);
        _lvFormats.Columns.Add("Ext", 60);
        _lvFormats.Columns.Add("Resolution", 220);
        _lvFormats.Columns.Add("FPS", 50);
        _lvFormats.Columns.Add("Size", 100);
        _lvFormats.Columns.Add("Type", 80);
        _lvFormats.Columns.Add("Tags", 200);
        _lvFormats.SelectedIndexChanged += (_, _) =>
        {
            _btnDownload.Enabled = _lvFormats.SelectedItems.Count > 0 && _cts == null;
        };

        _progress = new ProgressBar
        {
            Location = new Point(12, 384), Size = new Size(796, 18),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Minimum = 0, Maximum = 100
        };

        _lblProgress = new Label
        {
            Text = "Idle", Location = new Point(12, 406), AutoSize = true,
            ForeColor = Theme.TextMuted, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _log = new TextBox
        {
            Location = new Point(12, 428), Size = new Size(796, 80),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F)
        };

        _btnOpenFolder = new Button
        {
            Text = T("stream.openFolder"), Location = new Point(12, 518), Size = new Size(96, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left, BackColor = Color.White,
            FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text
        };
        _btnOpenFolder.FlatAppearance.BorderColor = Theme.Border;
        _btnOpenFolder.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_outputDirectory) && Directory.Exists(_outputDirectory))
                System.Diagnostics.Process.Start("explorer.exe", _outputDirectory);
        };

        _btnClose = new Button
        {
            Text = T("dlg.close"), Location = new Point(710, 518), Size = new Size(98, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, BackColor = Color.White,
            FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text
        };
        _btnClose.FlatAppearance.BorderColor = Theme.Border;
        _btnClose.Click += (_, _) =>
        {
            if (_cts != null) { try { _cts.Cancel(); } catch { } }
            Close();
        };

        AcceptButton = _btnList;
        CancelButton = _btnClose;

        Controls.Add(_txtUrl);
        Controls.Add(_btnList);
        Controls.Add(_btnGetTool);
        Controls.Add(_btnDownload);
        Controls.Add(_lblTool);
        Controls.Add(_lvFormats);
        Controls.Add(_progress);
        Controls.Add(_lblProgress);
        Controls.Add(_log);
        Controls.Add(_btnOpenFolder);
        Controls.Add(_btnClose);

        Load += async (_, _) =>
        {
            var tool = new StreamCapture();
            var path = tool.ResolveTool();
            if (path == null)
            {
                _lblTool.Text = T("stream.notFound");
                _lblTool.ForeColor = Color.FromArgb(176, 32, 32);
                _btnList.Enabled = false;
                _btnGetTool.Visible = true;
            }
            else
            {
                _lblTool.Text = "yt-dlp: " + path;
                _lblTool.ForeColor = Theme.TextMuted;
                if (!string.IsNullOrEmpty(initialUrl)) await ListFormatsAsync();
            }
        };

        FormClosing += (_, _) =>
        {
            if (_cts != null) { try { _cts.Cancel(); } catch { } }
        };
    }

    private async Task FetchToolAsync()
    {
        _btnGetTool.Enabled = false;
        _lblProgress.Text = T("stream.starting");
        try
        {
            var tool = new StreamCapture();
            var progress = new Progress<double>(pct =>
            {
                _progress.Value = Math.Clamp((int)Math.Round(pct), 0, 100);
                _lblProgress.Text = string.Format(T("stream.fetching"), pct);
            });
            var path = await tool.EnsureToolAsync(progress);
            _lblTool.Text = "yt-dlp: " + path;
            _lblTool.ForeColor = Theme.TextMuted;
            _log.AppendText("> yt-dlp fetched -> " + path + Environment.NewLine);

            _lblProgress.Text = T("stream.checkFfmpeg");
            try
            {
                var ff = await tool.EnsureFfmpegAsync(progress);
                _log.AppendText("> ffmpeg fetched -> " + ff + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _log.AppendText("! ffmpeg skipped: " + ex.Message + Environment.NewLine);
            }

            _btnGetTool.Visible = false;
            _btnList.Enabled = true;
            _lblProgress.Text = T("stream.toolReady");
            _log.AppendText("> yt-dlp fetched -> " + path + Environment.NewLine);
            if (!string.IsNullOrEmpty(_txtUrl.Text.Trim())) await ListFormatsAsync();
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Error: " + ex.Message;
            _log.AppendText("! " + ex.Message + Environment.NewLine);
            _btnGetTool.Enabled = true;
        }
    }

    private async Task ListFormatsAsync()
    {
        var url = _txtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        _lvFormats.Items.Clear();
        _log.AppendText("> yt-dlp -F " + url + Environment.NewLine);
        _btnList.Enabled = false;
        _btnDownload.Enabled = false;
        _lblProgress.Text = T("stream.enumerating");

        try
        {
            var tool = new StreamCapture();
            var formats = await tool.ListFormatsAsync(url);

            foreach (var f in formats.OrderByDescending(f => f.Height).ThenByDescending(f => f.Fps))
            {
                var item = new ListViewItem(f.Id);
                item.SubItems.Add(f.Extension);
                item.SubItems.Add(f.Resolution);
                item.SubItems.Add(f.Fps > 0 ? f.Fps.ToString() : "");
                item.SubItems.Add(f.Size);
                item.SubItems.Add(f.IsAudio ? "audio" : (f.Height >= 4320 ? "8K" : (f.Height >= 2160 ? "4K" : "video")));
                item.SubItems.Add(f.Height >= 4320 ? "8K" : (f.IsAudio ? "" : ""));
                item.Tag = f;
                _lvFormats.Items.Add(item);
            }
            _lblProgress.Text = $"{formats.Count} format(s) detected.";
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Error: " + ex.Message;
            _log.AppendText("! " + ex.Message + Environment.NewLine);
        }
        finally
        {
            _btnList.Enabled = true;
        }
    }

    private async Task DownloadAsync()
    {
        if (_lvFormats.SelectedItems.Count == 0) return;
        var format = _lvFormats.SelectedItems[0].Tag as StreamFormat;
        if (format == null) return;
        var url = _txtUrl.Text.Trim();

        _outputDirectory = Path.Combine(_manager.DownloadPath,
            "Streams", StreamCapture.Sanitize(DateTime.Now.ToString("yyyyMMdd-HHmmss")) + "-" + format.Id);
        Directory.CreateDirectory(_outputDirectory);

        _cts = new CancellationTokenSource();
        _btnList.Enabled = false;
        _btnDownload.Enabled = false;
        _progress.Value = 0;
        _lblProgress.Text = T("stream.starting");

        var progress = new Progress<StreamProgress>(p =>
        {
            _progress.Value = Math.Clamp((int)p.Percent, 0, 100);
            _lblProgress.Text = p.Line;
        });

        try
        {
            var tool = new StreamCapture();
            var savedPath = await tool.DownloadAsync(url, format.Id, _outputDirectory, progress, _cts.Token);
            if (savedPath != null && File.Exists(savedPath))
            {
                _progress.Value = 100;
                _lblProgress.Text = "Saved: " + savedPath;
                _log.AppendText("> saved -> " + savedPath + Environment.NewLine);
                _manager.RecordCompleted(savedPath, url, "stream");
            }
            else
            {
                _lblProgress.Text = "Done but file not found.";
            }
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Error: " + ex.Message;
            _log.AppendText("! " + ex.Message + Environment.NewLine);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _btnList.Enabled = true;
            _btnDownload.Enabled = _lvFormats.SelectedItems.Count > 0;
        }
    }
}