using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Per-category save folders. Empty means the
/// main download folder. Built in code (no designer file).
/// </summary>
public sealed class CategoryFoldersForm : Form
{
    private static string T(string key) => Localization.T(key);

    private static readonly string[] Categories =
        { "Video", "Audio", "Archive", "Document", "Application", "Image", "Torrent", "Streams", "Other" };

    private readonly AppSettings _settings;
    private readonly ListView _list;
    private readonly Button _btnBrowse;
    private readonly Button _btnClear;
    private readonly Button _btnReset;
    private readonly Button _ok;
    private readonly Button _cancel;

    public CategoryFoldersForm(AppSettings settings)
    {
        _settings = settings;

        Text = T("cf.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 360);
        ClientSize = new Size(600, 400);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Text = T("cf.hint"),
            AutoSize = true,
            Location = new Point(12, 12)
        };

        _list = new ListView
        {
            Location = new Point(12, 36),
            Size = new Size(576, 288),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _list.Columns.Add(T("cf.category"), 130);
        _list.Columns.Add(T("cf.folder"), 430);

        _btnBrowse = new Button
        {
            Text = T("dlg.browse"),
            Location = new Point(12, 334),
            Size = new Size(96, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnBrowse.Click += (_, _) => BrowseSelected();

        _btnClear = new Button
        {
            Text = T("cf.clear"),
            Location = new Point(114, 334),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnClear.Click += (_, _) => ClearSelected();

        _btnReset = new Button
        {
            Text = T("cf.resetAll"),
            Location = new Point(210, 334),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnReset.Click += (_, _) => Reload(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        _ok = new Button
        {
            Text = T("dlg.ok"),
            Location = new Point(404, 334),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _ok.Click += (_, _) => { Collect(); DialogResult = DialogResult.OK; Close(); };

        _cancel = new Button
        {
            Text = T("dlg.cancel"),
            Location = new Point(500, 334),
            Size = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.Add(hint);
        Controls.Add(_list);
        Controls.Add(_btnBrowse);
        Controls.Add(_btnClear);
        Controls.Add(_btnReset);
        Controls.Add(_ok);
        Controls.Add(_cancel);

        Reload(new Dictionary<string, string>(_settings.CategoryDirs, StringComparer.OrdinalIgnoreCase));
        _ok.Tag = "primary";
        Theme.StyleForm(this);
    }

    private string Effective(string category)
    {
        if (_settings.CategoryDirs.TryGetValue(category, out var dir) && !string.IsNullOrWhiteSpace(dir))
        {
            return dir;
        }
        return category == "Streams"
            ? Path.Combine(_settings.DownloadPath, "Streams")
            : _settings.DownloadPath;
    }

    private void Reload(Dictionary<string, string> values)
    {
        _list.Items.Clear();
        foreach (var category in Categories)
        {
            values.TryGetValue(category, out var dir);
            var item = new ListViewItem(Localization.T("cat." + category));
            item.SubItems.Add(string.IsNullOrWhiteSpace(dir) ? T("cf.main") : dir);
            item.Tag = category;
            _list.Items.Add(item);
        }
        _pending = values;
    }

    private Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);

    private void Collect()
    {
        _settings.CategoryDirs = new Dictionary<string, string>(_pending, StringComparer.OrdinalIgnoreCase);
    }

    private string? SelectedCategory()
    {
        if (_list.SelectedItems.Count == 0) return null;
        return _list.SelectedItems[0].Tag as string;
    }

    private void BrowseSelected()
    {
        var category = SelectedCategory();
        if (category == null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = string.Format("{0}: {1}", T("cf.folder"), category),
            UseDescriptionForTitle = true
        };
        try
        {
            var current = Effective(category);
            if (Directory.Exists(current)) dialog.SelectedPath = current;
        }
        catch
        {
            // Ignore a bad current path.
        }
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _pending[category] = dialog.SelectedPath;
        SelectedRow().SubItems[1].Text = dialog.SelectedPath;
    }

    private void ClearSelected()
    {
        var category = SelectedCategory();
        if (category == null) return;
        _pending.Remove(category);
        SelectedRow().SubItems[1].Text = T("cf.main");
    }

    private ListViewItem SelectedRow() => _list.SelectedItems[0];
}
