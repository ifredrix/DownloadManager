using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// File checksum verifier: computes the hash with progress and optionally
/// compares it against an expected value. Built in code (no designer file).
/// </summary>
public sealed class ChecksumForm : Form
{
    private static string T(string key) => Localization.T(key);

    private readonly string _filePath;
    private readonly ComboBox _cmbAlgo;
    private readonly TextBox _txtExpected;
    private readonly TextBox _txtActual;
    private readonly ProgressBar _progress;
    private readonly Label _lblResult;
    private readonly Button _btnCompute;
    private readonly Button _btnVerify;
    private readonly Button _btnClose;
    private CancellationTokenSource? _cts;
    private string _lastHash = string.Empty;
    private string _lastAlgo = "SHA256";

    public ChecksumForm(string fileName, string filePath)
    {
        _filePath = filePath;

        Text = T("hash.title") + " - " + fileName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 270);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lblAlgo = new Label { Text = T("hash.algo"), AutoSize = true, Location = new Point(16, 18) };
        _cmbAlgo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(160, 15),
            Size = new Size(150, 23)
        };
        _cmbAlgo.Items.AddRange(new object[] { "SHA256", "SHA1", "MD5" });
        _cmbAlgo.SelectedIndex = 0;

        _btnCompute = new Button
        {
            Text = T("hash.compute"), Location = new Point(322, 14), Size = new Size(110, 26),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnCompute.Click += async (_, _) => await ComputeAsync();

        var lblExpected = new Label { Text = T("hash.expected"), AutoSize = true, Location = new Point(16, 52) };
        _txtExpected = new TextBox
        {
            Location = new Point(160, 49), Size = new Size(384, 23),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 9F)
        };

        var lblActual = new Label { Text = T("hash.actual"), AutoSize = true, Location = new Point(16, 86) };
        _txtActual = new TextBox
        {
            Location = new Point(160, 83), Size = new Size(384, 23),
            ReadOnly = true, BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 9F)
        };

        _progress = new ProgressBar
        {
            Location = new Point(16, 118), Size = new Size(528, 18),
            Minimum = 0, Maximum = 100,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _lblResult = new Label
        {
            Text = string.Empty, AutoSize = true, Location = new Point(16, 144),
            Font = new Font(Font, FontStyle.Bold)
        };

        _btnVerify = new Button
        {
            Text = T("hash.verify"), Location = new Point(352, 216), Size = new Size(100, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnVerify.Click += (_, _) => Verify();

        _btnClose = new Button
        {
            Text = T("dlg.close"), Location = new Point(458, 216), Size = new Size(86, 30),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnClose.Click += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            Close();
        };

        AcceptButton = _btnVerify;
        CancelButton = _btnClose;

        Controls.Add(lblAlgo);
        Controls.Add(_cmbAlgo);
        Controls.Add(_btnCompute);
        Controls.Add(lblExpected);
        Controls.Add(_txtExpected);
        Controls.Add(lblActual);
        Controls.Add(_txtActual);
        Controls.Add(_progress);
        Controls.Add(_lblResult);
        Controls.Add(_btnVerify);
        Controls.Add(_btnClose);

        FormClosing += (_, _) => { try { _cts?.Cancel(); } catch { } };

        _btnCompute.Tag = "primary";
        Theme.StyleForm(this);
    }

    private static HashAlgorithm CreateAlgo(string name) => name switch
    {
        "SHA1" => SHA1.Create(),
        "MD5" => MD5.Create(),
        _ => SHA256.Create()
    };

    private async Task ComputeAsync()
    {
        _cts = new CancellationTokenSource();
        _btnCompute.Enabled = false;
        _lblResult.Text = string.Empty;
        _progress.Value = 0;

        var algoName = _cmbAlgo.SelectedItem?.ToString() ?? "SHA256";
        try
        {
            var length = new FileInfo(_filePath).Length;
            var progress = new Progress<long>(done =>
            {
                _progress.Value = length > 0
                    ? (int)Math.Min(100, done * 100 / length)
                    : 0;
            });

            var hash = await Task.Run(() =>
            {
                using var algo = CreateAlgo(algoName);
                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                var buffer = new byte[4 * 1024 * 1024];
                long done = 0;
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    algo.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    ((IProgress<long>)progress).Report(done);
                }
                algo.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToHexString(algo.Hash!).ToLowerInvariant();
            }, _cts.Token);

            _lastHash = hash;
            _lastAlgo = algoName;
            _txtActual.Text = hash;
            _progress.Value = 100;
            Verify();
        }
        catch (OperationCanceledException)
        {
            _lblResult.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _lblResult.Text = ex.Message;
            _lblResult.ForeColor = Theme.Danger;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _btnCompute.Enabled = true;
        }
    }

    private void Verify()
    {
        var expected = _txtExpected.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(expected))
        {
            _lblResult.Text = string.Empty;
            return;
        }
        if (string.IsNullOrEmpty(_lastHash))
        {
            _lblResult.Text = T("hash.computeFirst");
            _lblResult.ForeColor = Theme.TextMuted;
            return;
        }
        if (string.Equals(expected, _lastHash, StringComparison.OrdinalIgnoreCase))
        {
            _lblResult.Text = string.Format(T("hash.match"), _lastAlgo);
            _lblResult.ForeColor = Color.FromArgb(42, 122, 59);
        }
        else
        {
            _lblResult.Text = T("hash.mismatch");
            _lblResult.ForeColor = Theme.Danger;
        }
    }
}
