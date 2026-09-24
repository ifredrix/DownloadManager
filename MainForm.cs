using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

public partial class MainForm : Form
{
    private DownloadManager _downloadManager = null!;
    private AppSettings _settings = null!;
    private NotifyIcon _tray = null!;

    /// <summary>True with --tray (autostart): begin minimized, tray only.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Set only for a real exit (tray menu, shutdown, --quit):
    /// plain UserClosing parks to the tray instead.</summary>
    private bool _allowExit;

    private bool _trayHintShown;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _clipboardTimer;
    private readonly System.Windows.Forms.Timer _scheduleTimer;
    private readonly System.Windows.Forms.Timer _gridSaveTimer;
    private bool _applyingGridColumns;
    private DateTime _lastScheduleStart = DateTime.MinValue;
    private DateTime _lastScheduleStop = DateTime.MinValue;
    private bool _afterActionFired;
    private DownloadTask? _selectedTask;
    private string _lastClipboard = string.Empty;
    private LocalCaptureServer? _captureServer;
    private TorManager? _torManager;
    private CancellationTokenSource? _torStartCts;
    private readonly ToolStripStatusLabel _captureChrome = new() { Text = "Chrome ✗", ForeColor = Color.FromArgb(140, 145, 155), Spring = false };
    private readonly ToolStripStatusLabel _captureFirefox = new() { Text = "Firefox ✗", ForeColor = Color.FromArgb(140, 145, 155), Spring = false };
    private readonly ToolStripStatusLabel _captureEdge = new() { Text = "Edge ✗", ForeColor = Color.FromArgb(140, 145, 155), Spring = false };
    private readonly ToolStripStatusLabel _captureOpera = new() { Text = "Opera ✗", ForeColor = Color.FromArgb(140, 145, 155), Spring = false };
    private readonly ToolStripStatusLabel _torStatus = new() { Text = "Tor ✗", ForeColor = Color.FromArgb(140, 145, 155), Spring = false };

    private bool _themeDecorated;
    private MenuStrip? _menu;
    private ToolStripMenuItem? _menuStart;
    private ToolStripMenuItem? _menuPause;
    private ToolStripMenuItem? _menuResume;
    private ToolStripMenuItem? _menuCancel;
    private ToolStripMenuItem? _menuRemove;
    private ToolStripMenuItem? _menuOpenFolder;
    private ToolStripMenuItem? _menuCopyUrl;
    private ToolStripMenuItem? _menuTorRoute;
    private ToolStripMenuItem? _menuAssoc;
    private ToolStripMenuItem? _menuSkinLight;
    private ToolStripMenuItem? _menuSkinDark;
    private ToolStripMenuItem? _menuLangId;
    private ToolStripMenuItem? _menuLangEn;
    private readonly Dictionary<string, ToolStripMenuItem> _menuItems = new();
    private readonly Dictionary<string, ProgressForm> _progressWindows = new();

    private static string T(string key) => Localization.T(key);

    private ToolStripMenuItem M(string key, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(T(key), null, onClick);
        _menuItems[key] = item;
        return item;
    }

    private void SetLang(string lang)
    {
        Localization.SetLang(lang);
        if (_settings != null)
        {
            _settings.Language = Localization.Lang;
            _settings.Save();
        }
        ApplyLanguage();
    }

    /// <summary>Re-reads every visible string for the current language.</summary>
    private void ApplyLanguage()
    {
        foreach (var pair in _menuItems)
        {
            pair.Value.Text = T(pair.Key);
        }

        btnAddUrl.Text = T("btn.addUrl");
        btnAddTorrent.Text = T("btn.addTorrent");
        btnStream.Text = T("btn.stream");
        btnGrabLinks.Text = T("btn.grabLinks");
        btnBrowsers.Text = T("btn.browsers");
        btnSettings.Text = T("btn.settings");
        btnStart.Text = T("btn.start");
        btnPause.Text = T("btn.pause");
        btnResume.Text = T("btn.resume");
        btnCancel.Text = T("btn.cancel");
        btnRemove.Text = T("btn.remove");
        btnOpenFolder.Text = T("btn.openFolder");
        lblSearch.Text = T("lbl.search");
        lblCategoryFilter.Text = T("lbl.category");

        dataGridView.Columns["FileName"].HeaderText = T("col.filename");
        dataGridView.Columns["Size"].HeaderText = T("col.size");
        dataGridView.Columns["Progress"].HeaderText = T("col.progress");
        dataGridView.Columns["Speed"].HeaderText = T("col.speed");
        dataGridView.Columns["ETA"].HeaderText = T("col.eta");
        dataGridView.Columns["Status"].HeaderText = T("col.status");
        dataGridView.Columns["Type"].HeaderText = T("col.type");
        dataGridView.Columns["Category"].HeaderText = T("col.category");

        cmbCategoryFilter.Items.Clear();
        foreach (var key in CategoryKeys)
        {
            cmbCategoryFilter.Items.Add(T("cat." + key));
        }
        cmbCategoryFilter.SelectedIndex = 0;

        if (_tray != null) _tray.Text = T("lbl.tray");
        SyncSkinMenu();
        SyncLangMenu();
        BuildRowMenu();
        SetStatus(T("lbl.ready"));
        RefreshDataGridView();
        UpdateStatusInfo();
    }

    private void SyncLangMenu()
    {
        var id = Localization.Lang != "en";
        if (_menuLangId != null) _menuLangId.Checked = id;
        if (_menuLangEn != null) _menuLangEn.Checked = !id;
    }

    public MainForm()
    {
        InitializeComponent();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        var historyAutosaveTicks = 0;
        _uiTimer.Tick += (_, _) =>
        {
            UpdateStatusInfo();
            UpdateCaptureIndicators();
            // History persisted only on FormClosing and explicit actions, so a
            // hard death mid-download dropped in-flight tasks. Autosave every
            // minute: at most one minute of progress can ever be at risk, and
            // the sidecar on disk still owns the real resume data.
            if (++historyAutosaveTicks < 60) return;
            historyAutosaveTicks = 0;
            try { _downloadManager?.SaveHistory(AppSettings.HistoryFile); } catch { }
        };

        _clipboardTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        _scheduleTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _scheduleTimer.Tick += ScheduleTimer_Tick;

        _gridSaveTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _gridSaveTimer.Tick += (_, _) =>
        {
            _gridSaveTimer.Stop();
            SaveGridColumns();
            _settings?.Save();
        };

        btnAddUrl.Tag = "primary";
        Theme.Apply("Light", "Blue");
        ApplyTheme();
        SetupColumnHeaders();
        SetupMenuBar();
    }

    /// <summary>Menu bar directly under the native window caption.</summary>
    private void SetupMenuBar()
    {
        _menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden
        };

        var file = new ToolStripMenuItem(T("menu.file"));
        var addUrl = new ToolStripMenuItem(T("menu.addUrl"), null, (_, _) => { ActiveControl = txtUrl; txtUrl.Focus(); })
        {
            ShortcutKeys = Keys.Control | Keys.U
        };
        file.DropDownItems.Add(addUrl);
        file.DropDownItems.Add(M("menu.addBatch", (_, _) => _ = AddBatchAsync()));
        file.DropDownItems.Add(M("menu.exportQueue", (_, _) => ExportQueue()));
        file.DropDownItems.Add(M("menu.importQueue", (_, _) => ImportQueue()));
        file.DropDownItems.Add(M("menu.addTorrent", (_, _) => btnAddTorrent_Click(this, EventArgs.Empty)));
        file.DropDownItems.Add(M("menu.grabLinks", (_, _) => btnGrabLinks_Click(this, EventArgs.Empty)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(M("menu.exit", (_, _) => Close()));

        var downloads = new ToolStripMenuItem(T("menu.downloads"));
        _menuStart = M("menu.start", (_, _) => btnStart_Click(this, EventArgs.Empty));
        _menuPause = M("menu.pause", (_, _) => btnPause_Click(this, EventArgs.Empty));
        _menuResume = M("menu.resume", (_, _) => btnResume_Click(this, EventArgs.Empty));
        _menuCancel = M("menu.cancel", (_, _) => btnCancel_Click(this, EventArgs.Empty));
        _menuRemove = M("menu.remove", (_, _) => btnRemove_Click(this, EventArgs.Empty));
        downloads.DropDownItems.Add(_menuStart);
        downloads.DropDownItems.Add(_menuPause);
        downloads.DropDownItems.Add(_menuResume);
        downloads.DropDownItems.Add(_menuCancel);
        downloads.DropDownItems.Add(_menuRemove);
        downloads.DropDownItems.Add(M("menu.progress", (_, _) => OpenProgress()));
        downloads.DropDownItems.Add(M("menu.checksum", (_, _) => OpenChecksum()));
        _menuTorRoute = M("menu.torRoute", (_, _) => _ = ToggleTorRouteAsync());
        _menuTorRoute.CheckOnClick = true;
        downloads.DropDownItems.Add(_menuTorRoute);
        downloads.DropDownItems.Add(new ToolStripSeparator());
        var menuRefresh = M("menu.refresh", (_, _) => _ = RefreshSelectedAsync());
        downloads.DropDownItems.Add(menuRefresh);
        var menuLimit = M("menu.limit", (_, _) => OpenSpeedLimit());
        downloads.DropDownItems.Add(menuLimit);
        var menuProperties = M("menu.properties", (_, _) => OpenProperties());
        downloads.DropDownItems.Add(menuProperties);
        downloads.DropDownItems.Add(new ToolStripSeparator());
        _menuOpenFolder = M("menu.openFolder", (_, _) => btnOpenFolder_Click(this, EventArgs.Empty));
        _menuCopyUrl = M("menu.copyUrl", (_, _) =>
        {
            if (_selectedTask == null) return;
            try { Clipboard.SetText(_selectedTask.Url); } catch { }
        });
        downloads.DropDownItems.Add(_menuOpenFolder);
        downloads.DropDownItems.Add(_menuCopyUrl);

        var view = new ToolStripMenuItem(T("menu.view"));
        _menuSkinLight = M("menu.skinLight", (_, _) => SetSkin("Light"));
        _menuSkinDark = M("menu.skinDark", (_, _) => SetSkin("Dark"));
        view.DropDownItems.Add(_menuSkinLight);
        view.DropDownItems.Add(_menuSkinDark);
        view.DropDownItems.Add(new ToolStripSeparator());
        _menuLangId = M("menu.langId", (_, _) => SetLang("id"));
        _menuLangEn = M("menu.langEn", (_, _) => SetLang("en"));
        var langMenu = new ToolStripMenuItem(T("menu.language"));
        langMenu.DropDownItems.Add(_menuLangId);
        langMenu.DropDownItems.Add(_menuLangEn);
        _menuItems["menu.language"] = langMenu;
        view.DropDownItems.Add(langMenu);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(M("menu.customToolbar", (_, _) => OpenToolbarEditor()));
        view.DropDownItems.Add(M("menu.browsers", (_, _) => btnBrowsers_Click(this, EventArgs.Empty)));
        view.DropDownItems.Add(M("menu.resetColumns", (_, _) => ResetGridColumns()));

        var tools = new ToolStripMenuItem(T("menu.tools"));
        tools.DropDownItems.Add(M("menu.streamUrl", (_, _) => btnStream_Click(this, EventArgs.Empty)));
        tools.DropDownItems.Add(M("menu.torSetup", (_, _) => OpenTorSetup()));
        tools.DropDownItems.Add(M("menu.proxy", (_, _) => OpenProxy()));
        tools.DropDownItems.Add(M("menu.siteLogins", (_, _) => OpenSiteLogins()));
        tools.DropDownItems.Add(M("menu.catFolders", (_, _) => OpenCategoryFolders()));
        tools.DropDownItems.Add(M("menu.exclusions", (_, _) => OpenExclusions()));
        _menuAssoc = M("menu.assoc", (_, _) => ToggleAssoc());
        _menuAssoc.CheckOnClick = true;
        tools.DropDownItems.Add(_menuAssoc);
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(M("menu.settings", (_, _) => btnSettings_Click(this, EventArgs.Empty)));
        tools.DropDownOpening += (_, _) =>
        {
            if (_menuAssoc != null) _menuAssoc.Checked = FileAssoc.IsAssociated();
        };

        var help = new ToolStripMenuItem(T("menu.help"));
        help.DropDownItems.Add(M("menu.about", (_, _) => ShowAbout()));
        help.DropDownItems.Add(M("menu.updates", (_, _) => OpenUpdates()));

        _menuItems["menu.file"] = file;
        _menuItems["menu.downloads"] = downloads;
        _menuItems["menu.view"] = view;
        _menuItems["menu.tools"] = tools;
        _menuItems["menu.help"] = help;

        _menu.Items.Add(file);
        _menu.Items.Add(downloads);
        _menu.Items.Add(view);
        _menu.Items.Add(tools);
        _menu.Items.Add(help);
        Controls.Add(_menu);
        _menu.BringToFront();
        StyleMenu();
    }

    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;
        public override Color MenuItemPressedGradientBegin => Theme.AccentSoft;
        public override Color MenuItemPressedGradientEnd => Theme.AccentSoft;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Border;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color ImageMarginRevealedGradientBegin => Theme.Surface;
        public override Color ImageMarginRevealedGradientEnd => Theme.Surface;
        public override Color ToolStripBorder => Theme.Border;
        public override Color GripDark => Theme.Border;
        public override Color GripLight => Theme.Surface;
        public override Color StatusStripGradientBegin => Theme.SurfaceAlt;
        public override Color StatusStripGradientEnd => Theme.SurfaceAlt;
        public override Color ToolStripGradientBegin => Theme.SurfaceAlt;
        public override Color ToolStripGradientEnd => Theme.SurfaceAlt;
        public override Color MenuStripGradientBegin => Theme.SurfaceAlt;
        public override Color MenuStripGradientEnd => Theme.SurfaceAlt;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
    }

    /// <summary>Recolors the menu bar for the current skin (re-applied on change).</summary>
    private void StyleMenu()
    {
        if (_menu == null) return;
        _menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
        _menu.BackColor = Theme.SurfaceAlt;
        _menu.ForeColor = Theme.Text;
        foreach (ToolStripItem top in _menu.Items)
        {
            top.ForeColor = Theme.Text;
            if (top is ToolStripMenuItem parent) StyleMenuItems(parent.DropDownItems);
        }
    }

    private static void StyleMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = Theme.Text;
            item.BackColor = Theme.Surface;
            if (item is ToolStripMenuItem parent) StyleMenuItems(parent.DropDownItems);
        }
    }

    private void SetSkin(string theme)
    {
        if (_settings == null) return;
        _settings.ThemeMode = theme;
        ApplyTheme();
        _settings.Save();
        SyncSkinMenu();
    }

    private void SyncSkinMenu()
    {
        var dark = string.Equals(_settings?.ThemeMode, "Dark", StringComparison.OrdinalIgnoreCase);
        if (_menuSkinLight != null) _menuSkinLight.Checked = !dark;
        if (_menuSkinDark != null) _menuSkinDark.Checked = dark;
    }

    private void OpenToolbarEditor()
    {
        if (_settings == null) return;
        var order = new List<string>(_settings.ToolbarOrder);
        var hidden = new HashSet<string>(_settings.ToolbarHidden ?? new(), StringComparer.OrdinalIgnoreCase);
        using var dialog = new ToolbarEditorForm(order, hidden);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.ToolbarOrder = dialog.Order;
            _settings.ToolbarHidden = dialog.Hidden.ToList();
            ApplyToolbar();
            _settings.Save();
        }
    }

    private void ResetGridColumns()
    {
        if (_settings == null) return;
        _settings.GridColumns = GridColumnState.Defaults();
        ApplyGridColumns();
        SaveGridColumns();
        _settings.Save();
    }

    private void OpenTorSetup()
    {
        if (_settings == null) return;
        using var dialog = new TorSettingsForm(
            _settings,
            () => ApplyTorMode(),
            () => (_torManager is { IsRunning: true } m && !m.IsReady) ? m.BootstrapPercent : -1);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Save();
            ApplyTorMode();
        }
    }

    private void OpenProxy()
    {
        if (_settings == null) return;
        using var dialog = new ProxyForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings();
            _settings.Save();
            SetStatus(T("status.proxySaved"));
        }
        else
        {
            ApplySettings();
        }
    }

    private void ToggleAssoc()
    {
        try
        {
            var target = !FileAssoc.IsAssociated();
            FileAssoc.SetAssociated(target);
            SetStatus(T(target ? "status.assocOn" : "status.assocOff"));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.failed"), ex.Message));
        }
        if (_menuAssoc != null) _menuAssoc.Checked = FileAssoc.IsAssociated();
    }

    private void OpenSiteLogins()
    {
        if (_settings == null) return;
        using var dialog = new SiteLoginsForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings();
            _settings.Save();
            SetStatus(T("status.loginSaved"));
        }
    }

    private void OpenCategoryFolders()
    {
        if (_settings == null) return;
        using var dialog = new CategoryFoldersForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings();
            _settings.Save();
            SetStatus(T("status.catSaved"));
        }
    }

    private void OpenUpdates()
    {
        if (_settings == null) return;
        using var dialog = new UpdateForm(_settings);
        dialog.ShowDialog(this);
        _settings.Save();
    }

    private void OpenExclusions()
    {
        if (_settings == null) return;
        using var dialog = new ExclusionsForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings();
            _settings.Save();
            SetStatus(T("status.exSaved"));
        }
    }

    private void ShowAbout()
    {
        // A custom form, not MessageBox: the system dialog insists on the
        // stock Windows icon and cannot show Theme.AppIcon.
        using var dialog = new AboutForm();
        dialog.ShowDialog(this);
    }

    private void ApplyTheme()
    {
        if (_settings != null)
        {
            Theme.Apply(_settings.ThemeMode, _settings.AccentName);
        }

        BackColor = Theme.WindowBack;
        ForeColor = Theme.Text;
        Theme.StyleForm(this);
        Theme.StyleGrid(dataGridView);
        StyleMenu();

        if (!_themeDecorated)
        {
            _themeDecorated = true;
            statusStrip.Items.Add(_captureChrome);
            statusStrip.Items.Add(_captureFirefox);
            statusStrip.Items.Add(_captureEdge);
            statusStrip.Items.Add(_captureOpera);
            statusStrip.Items.Add(_torStatus);
        }

        if (_downloadManager != null) RefreshDataGridView();
    }

    private Button? ToolbarButton(string key) => key switch
    {
        "AddUrl" => btnAddUrl,
        "AddTorrent" => btnAddTorrent,
        "Stream" => btnStream,
        "GrabLinks" => btnGrabLinks,
        "Browsers" => btnBrowsers,
        "Settings" => btnSettings,
        _ => null,
    };

    private static readonly string[] CanonicalToolbar =
        { "AddUrl", "AddTorrent", "Stream", "GrabLinks", "Browsers", "Settings" };

    private static string ToolbarTitle(string key) =>
        ToolbarEditorForm.ButtonTitles.TryGetValue(key, out var title) ? title : key;

    /// <summary>Shows/hides top-toolbar buttons and packs the visible ones right-aligned.</summary>
    private void ApplyToolbar()
    {
        if (_settings == null) return;

        var order = _settings.ToolbarOrder
            .Where(k => ToolbarButton(k) != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var key in CanonicalToolbar)
        {
            if (!order.Contains(key, StringComparer.OrdinalIgnoreCase)) order.Add(key);
        }

        var hidden = new HashSet<string>(_settings.ToolbarHidden ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in order)
        {
            if (ToolbarButton(key) is { } btn) btn.Visible = !hidden.Contains(key);
        }

        LayoutTopToolbar();
    }

    private void LayoutTopToolbar()
    {
        if (_settings == null) return;

        const int top = 40;
        const int margin = 12;
        const int gap = 8;

        var order = _settings.ToolbarOrder
            .Where(k => ToolbarButton(k) != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var key in CanonicalToolbar)
        {
            if (!order.Contains(key, StringComparer.OrdinalIgnoreCase)) order.Add(key);
        }

        var hidden = new HashSet<string>(_settings.ToolbarHidden ?? new(), StringComparer.OrdinalIgnoreCase);
        var x = ClientSize.Width - margin;
        var firstLeft = x;

        // Pack right-aligned but keep list order left-to-right (Add URL first,
        // next to the URL box; Settings last at the far right).
        foreach (var key in order.AsEnumerable().Reverse())
        {
            if (ToolbarButton(key) is not { } btn) continue;
            if (hidden.Contains(key)) { btn.Visible = false; continue; }
            btn.Visible = true;
            btn.Location = new Point(x - btn.Width, top);
            btn.Size = new Size(btn.Width, 30);
            x -= btn.Width + gap;
            firstLeft = btn.Left;
        }

        // The URL box always matches button height and vertical position, and
        // stretches up to the leftmost visible button.
        txtUrl.Location = new Point(txtUrl.Left, top);
        txtUrl.Size = new Size(Math.Max(100, firstLeft - gap - txtUrl.Left), btnAddUrl.Height);
    }

    private void SetupColumnHeaders()
    {
        // Make it obvious that the column headers can be grabbed to reorder.
        dataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GridHeaderBack;
        dataGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.GridHeaderText;
        AttachGrabCursor(dataGridView);
    }

    private static void AttachGrabCursor(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GridHeaderBack;

        grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex == -1) grid.Cursor = Cursors.SizeAll;
        };
        grid.CellMouseLeave += (_, e) =>
        {
            if (e.RowIndex == -1) grid.Cursor = Cursors.Default;
        };
        grid.MouseMove += (_, e) =>
        {
            // Over a resize edge (last 4 px of a column) the platform already
            // changes the cursor to SizeWE; everywhere else on the header we
            // want the four-arrow "drag" cursor.
            var info = grid.HitTest(e.X, e.Y);
            if (info.Type == DataGridViewHitTestType.ColumnHeader)
            {
                var xInHeader = e.X - info.ColumnX;
                var col = grid.Columns[info.ColumnIndex];
                if (xInHeader > col.Width - 5)
                {
                    grid.Cursor = Cursors.SizeWE; // resize handle
                }
                else
                {
                    grid.Cursor = Cursors.SizeAll; // drag to reorder
                }
            }
            else
            {
                grid.Cursor = Cursors.Default;
            }
        };
    }



    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTopToolbar();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
    }

    private void InitializeDownloadManager()
    {
        Directory.CreateDirectory(_settings.DownloadPath);

        TorProxy.Host = _settings.TorHost;
        TorProxy.Port = _settings.TorPort;
        NetConfig.ProxyMode = _settings.ProxyMode;
        NetConfig.ProxyHost = _settings.ProxyHost;
        NetConfig.ProxyPort = _settings.ProxyPort;
        NetConfig.ProxyUser = _settings.ProxyUser;
        NetConfig.ProxyPass = _settings.ProxyPass;
        NetConfig.SiteLogins = new Dictionary<string, SiteLogin>(
            _settings.SiteLogins, StringComparer.OrdinalIgnoreCase);

        _downloadManager = new DownloadManager(_settings.DownloadPath, _settings.MaxConcurrentDownloads)
        {
            ConnectionsPerDownload = _settings.ConnectionsPerDownload,
            SpeedLimitBytesPerSecond = _settings.SpeedLimitBytesPerSecond,
            RouteAllViaTor = _settings.TorRouteAll,
            CategoryDirs = new Dictionary<string, string>(_settings.CategoryDirs, StringComparer.OrdinalIgnoreCase)
        };

        _captureServer = new LocalCaptureServer(_downloadManager);
        _captureServer.IsExcluded = url => Exclusions.IsExcluded(url, _settings.ExcludedPatterns);
        if (_captureServer.TryStart())
        {
            _captureServer.LinkCaptured += OnBrowserLinkCaptured;
            _captureServer.ShowRequested += (_, _) => RestoreFromTray();
            _captureServer.QuitRequested += (_, _) => Ui(() => { _allowExit = true; Close(); });
            SetStatus(string.Format(T("status.captureOk"), _captureServer.Port));
        }
        else
        {
            SetStatus(T("status.captureFail"));
        }

        _downloadManager.TaskAdded += OnTaskAdded;
        _downloadManager.TaskUpdated += OnTaskUpdated;
        _downloadManager.TaskCompleted += OnTaskCompleted;
        _downloadManager.TaskFailed += OnTaskFailed;
        _downloadManager.TaskRemoved += OnTaskRemoved;
    }

    private void SetupDataGridView()
    {
        dataGridView.Columns.Clear();
        dataGridView.AllowUserToOrderColumns = true;
        dataGridView.AllowUserToResizeColumns = true;
        dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

        // Fixed widths: predictable dividers and drag-reorder.
        // (A Fill column auto-compensates and makes neighbours grow oddly.)
        var fileName = new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            HeaderText = "File Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 260,
            MinimumWidth = 160,
            Resizable = DataGridViewTriState.True
        };

        dataGridView.Columns.Add(fileName);
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Size", HeaderText = "Size", Width = 80,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewProgressColumn
        {
            Name = "Progress", HeaderText = "Progress", Width = 150,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Speed", HeaderText = "Speed", Width = 95,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ETA", HeaderText = "ETA", Width = 70,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status", HeaderText = "Status", Width = 90,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type", HeaderText = "Type", Width = 70,
            Resizable = DataGridViewTriState.True
        });
        dataGridView.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Category", HeaderText = "Category", Width = 85,
            Resizable = DataGridViewTriState.True
        });

        ApplyGridColumns();
        dataGridView.ColumnHeaderMouseClick += DataGridView_ColumnHeaderMouseClick;
        dataGridView.CellMouseDown += DataGridView_CellMouseDown;
        dataGridView.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _selectedTask != null)
            {
                e.Handled = true;
                OpenProperties();
            }
        };
        BuildRowMenu();
        dataGridView.ColumnDisplayIndexChanged += (_, _) =>
        {
            // User dragged a column. Debounced so transitional states
            // during the drag collapse into a single save.
            if (_applyingGridColumns) return;
            _gridSaveTimer.Stop();
            _gridSaveTimer.Start();
        };
        dataGridView.ColumnWidthChanged += (_, _) =>
        {
            // Debounced: fires per-pixel while resizing.
            if (_applyingGridColumns) return;
            _gridSaveTimer.Stop();
            _gridSaveTimer.Start();
        };
    }

    /// <summary>Restores saved column width, visibility and order.</summary>
    private void ApplyGridColumns()
    {
        if (_settings == null) return;

        _applyingGridColumns = true;
        try
        {
            foreach (var state in _settings.GridColumns)
            {
                if (!dataGridView.Columns.Contains(state.Name)) continue;
                var col = dataGridView.Columns[state.Name];
                col.Width = Math.Clamp(state.Width, 40, 800);
                col.Visible = state.Visible;
            }

            var ordered = _settings.GridColumns
                .Where(s => dataGridView.Columns.Contains(s.Name))
                .OrderBy(s => s.DisplayIndex)
                .Select(s => s.Name)
                .ToList();
            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                if (!ordered.Contains(col.Name)) ordered.Add(col.Name);
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                dataGridView.Columns[ordered[i]].DisplayIndex = i;
            }
        }
        finally
        {
            _applyingGridColumns = false;
        }
    }

    private void SaveGridColumns()
    {
        if (_settings == null) return;

        _settings.GridColumns = dataGridView.Columns
            .Cast<DataGridViewColumn>()
            .Select(c => new GridColumnState
            {
                Name = c.Name,
                Visible = c.Visible,
                Width = c.Width,
                DisplayIndex = c.DisplayIndex
            })
            .ToList();
    }

    private ContextMenuStrip? _rowMenu;
    private ToolStripMenuItem? _rowTorRoute;

    private void BuildRowMenu()
    {
        _rowMenu?.Dispose();
        _rowMenu = new ContextMenuStrip();
        _rowMenu.Items.Add(T("menu.start"), null, (_, _) => { if (_selectedTask != null) _ = _downloadManager.StartDownloadAsync(_selectedTask.Id); });
        _rowMenu.Items.Add(T("menu.pause"), null, (_, _) => { if (_selectedTask != null) _downloadManager.PauseDownload(_selectedTask.Id); });
        _rowMenu.Items.Add(T("menu.resume"), null, (_, _) => { if (_selectedTask != null) _downloadManager.ResumeDownload(_selectedTask.Id); });
        _rowMenu.Items.Add(T("menu.cancel"), null, (_, _) => { if (_selectedTask != null) { _downloadManager.CancelDownload(_selectedTask.Id); UpdateButtonStates(); } });
        _rowMenu.Items.Add(T("menu.remove"), null, (_, _) => RemoveSelectedTask());
        _rowMenu.Items.Add(T("menu.progress"), null, (_, _) => OpenProgress());
        _rowMenu.Items.Add(T("menu.checksum"), null, (_, _) => OpenChecksum());
        _rowTorRoute = new ToolStripMenuItem(T("menu.torRoute"), null, (_, _) => _ = ToggleTorRouteAsync())
        {
            CheckOnClick = true
        };
        _rowMenu.Items.Add(_rowTorRoute);
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add(T("row.refresh"), null, (_, _) => _ = RefreshSelectedAsync());
        _rowMenu.Items.Add(T("menu.limit"), null, (_, _) => OpenSpeedLimit());
        _rowMenu.Items.Add(T("row.properties"), null, (_, _) => OpenProperties());
        _rowMenu.Items.Add(T("row.openFolder"), null, (_, _) => btnOpenFolder_Click(this, EventArgs.Empty));
        _rowMenu.Items.Add(T("row.copyUrl"), null, (_, _) =>
        {
            if (_selectedTask == null) return;
            try { Clipboard.SetText(_selectedTask.Url); } catch { }
        });
        _rowMenu.Opening += (_, e) =>
        {
            if (_selectedTask == null) { e.Cancel = true; return; }
            var status = _selectedTask.Status;
            SetMenu(_rowMenu!, 0, status is DownloadStatus.Pending or DownloadStatus.Paused or DownloadStatus.Error or DownloadStatus.Cancelled);
            SetMenu(_rowMenu!, 1, status is DownloadStatus.Downloading or DownloadStatus.Queued);
            SetMenu(_rowMenu!, 2, status is DownloadStatus.Paused or DownloadStatus.Error);
            SetMenu(_rowMenu!, 3, status is DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Paused);
            SetMenu(_rowMenu!, 4, true);
        };
        dataGridView.ContextMenuStrip = _rowMenu;
    }

    private static void SetMenu(ContextMenuStrip menu, int index, bool enabled)
    {
        if (menu.Items.Count > index) menu.Items[index].Enabled = enabled;
    }

    private void DataGridView_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
        dataGridView.ClearSelection();
        dataGridView.Rows[e.RowIndex].Selected = true;
    }

    /// <summary>Guaranteed column move (menu alternative to header drag).</summary>
    private void MoveColumn(DataGridViewColumn column, int delta)
    {
        var ordered = dataGridView.Columns.Cast<DataGridViewColumn>()
            .OrderBy(c => c.DisplayIndex).ToList();
        var index = ordered.IndexOf(column);
        var other = index + delta;
        if (index < 0 || other < 0 || other >= ordered.Count) return;

        (ordered[index], ordered[other]) = (ordered[other], ordered[index]);
        _applyingGridColumns = true;
        try
        {
            for (var i = 0; i < ordered.Count; i++) ordered[i].DisplayIndex = i;
        }
        finally
        {
            _applyingGridColumns = false;
        }
        SaveGridColumns();
        _settings?.Save();
    }

    private void DataGridView_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var menu = new ContextMenuStrip();

        if (e.ColumnIndex >= 0)
        {
            var target = dataGridView.Columns[e.ColumnIndex];
            var moveLeft = new ToolStripMenuItem(string.Format(T("hdr.moveLeft"), target.HeaderText));
            moveLeft.Click += (_, _) => MoveColumn(target, -1);
            var moveRight = new ToolStripMenuItem(string.Format(T("hdr.moveRight"), target.HeaderText));
            moveRight.Click += (_, _) => MoveColumn(target, +1);
            menu.Items.Add(moveLeft);
            menu.Items.Add(moveRight);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (DataGridViewColumn col in dataGridView.Columns
                     .Cast<DataGridViewColumn>()
                     .OrderBy(c => c.DisplayIndex))
        {
            var captured = col;
            var item = new ToolStripMenuItem(captured.HeaderText)
            {
                Checked = captured.Visible,
                CheckOnClick = true
            };
            item.Click += (_, _) =>
            {
                // Always keep at least one column visible.
                if (!item.Checked && dataGridView.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible) <= 1)
                {
                    item.Checked = true;
                    return;
                }
                captured.Visible = item.Checked;
                SaveGridColumns();
                _settings?.Save();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var reset = new ToolStripMenuItem(T("hdr.reset"));
        reset.Click += (_, _) =>
        {
            _settings!.GridColumns = GridColumnState.Defaults();
            ApplyGridColumns();
            SaveGridColumns();
            _settings.Save();
        };
        menu.Items.Add(reset);

        var pos = dataGridView.PointToClient(Cursor.Position);
        menu.Show(dataGridView, pos);
    }

    // ------------------------------------------------------------- lifecycle ----

    private async void MainForm_Load(object sender, EventArgs e)
    {
        // Done here rather than in the constructor: font autoscaling runs after
        // InitializeComponent and would overwrite the height.
        txtUrl.Height = btnAddUrl.Height;

        _settings = AppSettings.Load();
        Localization.SetLang(_settings.Language);
        InitializeDownloadManager();
        SetupDataGridView();
        var historyLoaded = _downloadManager.LoadHistory(AppSettings.HistoryFile);
        AppLog.Info($"history loaded: {historyLoaded} entries");
        RefreshDataGridView();
        ApplyTheme();
        ApplyToolbar();
        SyncSkinMenu();
        ApplyLanguage();

        _tray = new NotifyIcon
        {
            Icon = Theme.AppIcon,
            Visible = true,
            Text = "ifredrix Download Manager"
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(new ToolStripMenuItem(T("tray.show"), null, (_, _) => RestoreFromTray()));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem(T("tray.quit"), null, (_, _) => { _allowExit = true; Close(); }));
        _tray.ContextMenuStrip = trayMenu;

        cmbCategoryFilter.SelectedIndex = 0;

        _uiTimer.Start();
        if (_settings.MonitorClipboard) _clipboardTimer.Start();
        _scheduleTimer.Start();
        ApplyTorMode();

        SetupDragDrop();

        if (StartupUrls.Count > 0)
        {
            var pending = StartupUrls.ToList();
            StartupUrls.Clear();
            foreach (var url in pending)
            {
                await RouteLinkAsync(url);
            }
        }

        UpdateButtonStates();
        UpdateStatusInfo();
        ActiveControl = txtUrl;

        if (StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }

    }

    private void ApplySettings()
    {
        _downloadManager.MaxConcurrentDownloads = _settings.MaxConcurrentDownloads;
        _downloadManager.ConnectionsPerDownload = _settings.ConnectionsPerDownload;
        _downloadManager.SpeedLimitBytesPerSecond = _settings.SpeedLimitBytesPerSecond;
        _downloadManager.DownloadPath = _settings.DownloadPath;
        _downloadManager.RouteAllViaTor = _settings.TorRouteAll;
        _downloadManager.CategoryDirs = new Dictionary<string, string>(
            _settings.CategoryDirs, StringComparer.OrdinalIgnoreCase);

        TorProxy.Host = _settings.TorHost;
        TorProxy.Port = _settings.TorPort;
        NetConfig.ProxyMode = _settings.ProxyMode;
        NetConfig.ProxyHost = _settings.ProxyHost;
        NetConfig.ProxyPort = _settings.ProxyPort;
        NetConfig.ProxyUser = _settings.ProxyUser;
        NetConfig.ProxyPass = _settings.ProxyPass;
        NetConfig.SiteLogins = new Dictionary<string, SiteLogin>(
            _settings.SiteLogins, StringComparer.OrdinalIgnoreCase);
        _downloadManager.ReloadConnection();
        ApplyTorMode();

        ApplyTheme();
        ApplyToolbar();
        SyncSkinMenu();

        if (_settings.MonitorClipboard) _clipboardTimer.Start();
        else _clipboardTimer.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_allowExit)
        {
            // X / Alt+F4 parks to the tray instead of exiting: the browser
            // panel needs the local server alive. A real exit comes from
            // the tray menu, Windows shutdown, or --quit.
            e.Cancel = true;
            HideToTray();
            return;
        }

        AppLog.Info($"closing ({e.CloseReason}): saving history");
        SaveGridColumns();
        try { _downloadManager?.SaveHistory(AppSettings.HistoryFile); }
        catch (Exception ex) { AppLog.Error("history save on close failed: " + ex); }
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _clipboardTimer.Stop();
        _clipboardTimer.Dispose();
        _scheduleTimer.Stop();
        _scheduleTimer.Dispose();
        _gridSaveTimer.Stop();
        _gridSaveTimer.Dispose();

        try { _torStartCts?.Cancel(); } catch { }
        try { _torManager?.Dispose(); } catch { }

        try
        {
            _captureServer?.Dispose();
        }
        catch { }

        try
        {
            _downloadManager?.Dispose();
        }
        catch
        {
            // ignore
        }

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _settings?.Save();
        AppLog.Info("closing complete");
        base.OnFormClosing(e);
    }

    // ----------------------------------------------------------------- add ----

    private async void btnAddUrl_Click(object sender, EventArgs e)
    {
        var url = txtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            SetStatus(T("status.pasteLink"));
            return;
        }

        // Magnet links go straight to the torrent engine.
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            txtUrl.Clear();
            await AddAsync(url, DownloadType.Torrent);
            return;
        }

        // A pasted local .torrent path goes straight to the torrent engine.
        if (IsLocalTorrentFile(url))
        {
            txtUrl.Clear();
            await AddAsync(url, DownloadType.Torrent);
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(T("status.badScheme"));
            return;
        }

        // FTP goes straight to the multi-connection FTP engine.
        if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        {
            txtUrl.Clear();
            await AddAsync(url, DownloadType.Regular, confirmDialog: true);
            return;
        }

        // MMS is handled as HTTP fallback by the download manager.
        if (url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
        {
            txtUrl.Clear();
            await AddAsync(url, DownloadType.Regular, confirmDialog: true);
            return;
        }

        // YouTube and other stream hosts auto-queue best MP4 in the background.
        if (IsKnownStreamingHost(url) || StreamCapture.IsPlaylistUrl(url))
        {
            txtUrl.Clear();
            await AddStreamFromUrlAsync(url);
            return;
        }

        // An http(s) link to a .torrent file: fetch it, then add as torrent.
        if (IsTorrentUrl(url))
        {
            txtUrl.Clear();
            await AddTorrentFromUrlAsync(url);
            return;
        }

        txtUrl.Clear();
        await AddAsync(url, DownloadType.Regular, confirmDialog: true);
    }

    private async void txtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            btnAddUrl_Click(sender, e);
        }
    }

    private async void btnAddTorrent_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Torrent files (*.torrent)|*.torrent|All files (*.*)|*.*",
            FilterIndex = 1,
            RestoreDirectory = true,
            Title = "Select a torrent file"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await AddAsync(dialog.FileName, DownloadType.Torrent);
    }

    private async System.Threading.Tasks.Task AddAsync(
        string source, DownloadType type, bool confirmDialog = false, bool quiet = false)
    {
        SetStatus(T("status.contacting"));
        try
        {
            if (type == DownloadType.Regular && confirmDialog && _settings.ConfirmNewDownload)
            {
                var probed = await _downloadManager.ProbeDownloadAsync(source);
                using var dialog = new NewDownloadForm(probed);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    SetStatus(T("status.addCancelled"));
                    return;
                }
                var task = await _downloadManager.AddProbedAsync(probed);
                SetStatus(task.UseTor
                    ? string.Format(T("status.queuedTor"), task.FileName)
                    : string.Format(T("status.queued"), task.FileName));
                return;
            }

            var direct = await _downloadManager.AddDownloadAsync(source, type);
            SetStatus(direct.UseTor
                ? string.Format(T("status.queuedTor"), direct.FileName)
                : string.Format(T("status.queued"), direct.FileName));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.failed"), ex.Message));
            if (!quiet)
            {
                MessageBox.Show(this, ex.Message, T("msg.addTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async void btnGrabLinks_Click(object sender, EventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            text = string.Empty;
        }

        var links = ExtractLinks(text);
        if (links.Count == 0)
        {
            // Nothing pastable: grab links from a page address instead.
            using var spider = new SpiderForm(text.Trim());
            spider.ShowDialog(this);
            if (spider.Found.Count == 0) return;

            var picked = LinkPickerForm.Show(this, spider.Found);
            if (picked.Count == 0) return;
            foreach (var link in picked) await RouteLinkAsync(link);
            return;
        }

        var pickedClipboard = LinkPickerForm.Show(this, links);
        if (pickedClipboard.Count == 0) return;

        foreach (var link in pickedClipboard)
        {
            await RouteLinkAsync(link);
        }
    }

    /// <summary>Sends one picked/captured link to the right engine.</summary>
    private async Task AddBatchAsync()
    {
        using var dialog = new BatchForm();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        foreach (var link in dialog.Links)
        {
            await RouteLinkAsync(link, quiet: true);
        }
        SetStatus(string.Format(T("batch.done"), dialog.Links.Count));
    }

    private void ExportQueue()
    {
        if (_downloadManager == null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "Queue files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "ifredrix-queue.json",
            Title = T("menu.exportQueue")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _downloadManager.ExportQueue(dialog.FileName);
            SetStatus(string.Format(T("status.exported"), dialog.FileName));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.exportFailed"), ex.Message));
        }
    }

    private void ImportQueue()
    {
        if (_downloadManager == null) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Queue files (*.json)|*.json|All files (*.*)|*.*",
            Title = T("menu.importQueue")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = _downloadManager.ImportQueue(dialog.FileName);
            _downloadManager.SaveHistory(AppSettings.HistoryFile);
            SetStatus(string.Format(T("status.imported"), count));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.importFailed"), ex.Message));
        }
    }

    private async Task RouteLinkAsync(string link, bool quiet = false)
    {
        if (IsLocalTorrentFile(link))
        {
            await AddAsync(link, DownloadType.Torrent, quiet: quiet);
            return;
        }

        if (IsKnownStreamingHost(link) || StreamCapture.IsPlaylistUrl(link))
        {
            await AddStreamFromUrlAsync(link, quiet);
            return;
        }

        if (link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            await AddAsync(link, DownloadType.Torrent, quiet: quiet);
            return;
        }

        if (IsTorrentUrl(link))
        {
            await AddTorrentFromUrlAsync(link, quiet);
            return;
        }

        await AddAsync(link, DownloadType.Regular, quiet: quiet);
    }

    public static List<string> ExtractLinks(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        foreach (Match match in Regex.Matches(text, @"magnet:\?[^\s""'<>]+", RegexOptions.IgnoreCase))
        {
            results.Add(match.Value);
        }

        foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase))
        {
            results.Add(match.Value.TrimEnd('.', ',', ';', ')', ']'));
        }

        foreach (Match match in Regex.Matches(text, @"(?:ftp|ftps|mms)://[^\s""'<>]+", RegexOptions.IgnoreCase))
        {
            results.Add(match.Value.TrimEnd('.', ',', ';', ')', ']'));
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.MonitorClipboard) return;

        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || text == _lastClipboard) return;
        _lastClipboard = text;

        var links = ExtractLinks(text);
        if (links.Count == 0) return;

        // Excluded URLs are silently skipped by automatic capture.
        var allowed = links.Where(l => !Exclusions.IsExcluded(l, _settings.ExcludedPatterns)).ToList();
        if (allowed.Count == 0) return;

        // Stream links auto-queue best MP4 like any other paste.
        SetStatus(string.Format(T("status.clipboardFound"), allowed.Count));
        foreach (var link in allowed)
        {
            if (IsKnownStreamingHost(link) || StreamCapture.IsPlaylistUrl(link))
            {
                _ = AddStreamFromUrlAsync(link);
            }
            else if (link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                _ = AddAsync(link, DownloadType.Torrent);
            }
            else if (IsTorrentUrl(link))
            {
                _ = AddTorrentFromUrlAsync(link);
            }
            else
            {
                _ = AddAsync(link, DownloadType.Regular);
            }
        }
    }

    private async System.Threading.Tasks.Task AddStreamFromUrlAsync(string url, bool quiet = false)
    {
        SetStatus(T("status.queuingStream"));
        try
        {
            var tool = new StreamCapture();
            if (tool.ResolveTool() == null)
            {
                var progress = new Progress<double>(pct =>
                    SetStatus(string.Format(T("status.fetchYtdlpPct"), pct)));
                SetStatus(T("status.fetchYtdlp"));
                await tool.EnsureToolAsync(progress).ConfigureAwait(true);
            }

            var task = await _downloadManager.AddStreamAsync(url).ConfigureAwait(true);
            SetStatus(string.Format(T("status.queuedStream"), task.FileName));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.failed"), ex.Message));
            if (!quiet)
            {
                MessageBox.Show(this, ex.Message, T("msg.streamTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public static bool IsLocalTorrentFile(string path)
    {
        try
        {
            var trimmed = path.Trim().Trim('"');
            return trimmed.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTorrentUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            var path = uri.LocalPath;
            if (path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return true;
            return url.Contains(".torrent", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void OpenStreamDialog(string url)
    {
        using var dialog = new StreamCaptureForm(_downloadManager, url);
        dialog.ShowDialog(this);
    }

    private async System.Threading.Tasks.Task AddTorrentFromUrlAsync(string url, bool quiet = false)
    {
        SetStatus(T("status.torrentFile"));
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ifredrixDownloadManager/2.0");
            var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(true);
            if (bytes.Length == 0) throw new InvalidOperationException("Empty .torrent file.");

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ifredrixDownloadManager", "cache", "torrents");
            Directory.CreateDirectory(cacheDir);
            var tempFile = Path.Combine(cacheDir, $"link-{DateTime.Now:yyyyMMdd-HHmmss-fff}.torrent");
            await File.WriteAllBytesAsync(tempFile, bytes).ConfigureAwait(true);

            await AddAsync(tempFile, DownloadType.Torrent).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.failed"), ex.Message));
            if (!quiet)
            {
                MessageBox.Show(this, ex.Message, T("msg.torrentTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async System.Threading.Tasks.Task RefreshTorIndicatorAsync()
    {
        var mode = _settings?.TorMode ?? "Off";
        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            Ui(() =>
            {
                _torStatus.Text = T("tor.off");
                _torStatus.ForeColor = Color.FromArgb(140, 145, 155);
                _torStatus.ToolTipText = T("tor.tipOff");
            });
            return;
        }

        if (string.Equals(mode, "Managed", StringComparison.OrdinalIgnoreCase) &&
            _torManager is { IsRunning: true } manager && !manager.IsReady)
        {
            Ui(() =>
            {
                _torStatus.Text = $"Tor {manager.BootstrapPercent}%";
                _torStatus.ForeColor = Color.FromArgb(180, 130, 40);
                _torStatus.ToolTipText = T("tor.tipBoot");
            });
            return;
        }

        bool ok;
        try
        {
            ok = await TorProxy.IsAvailableAsync().ConfigureAwait(true);
        }
        catch
        {
            ok = false;
        }

        Ui(() =>
        {
            _torStatus.Text = ok ? "Tor ✓" : "Tor ✗";
            _torStatus.ForeColor = ok
                ? Color.FromArgb(42, 122, 59)
                : Color.FromArgb(140, 145, 155);
            _torStatus.ToolTipText = ok
                ? string.Format(T("tor.tipOk"), TorProxy.Host, TorProxy.Port)
                : T("tor.tipMissing");
        });
    }

    /// <summary>Starts/stops the managed tor.exe to match settings.</summary>
    private void ApplyTorMode()
    {
        try { _torStartCts?.Cancel(); } catch { }
        _torStartCts = new CancellationTokenSource();
        var ct = _torStartCts.Token;

        var mode = _settings?.TorMode ?? "Off";
        if (!string.Equals(mode, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            try { _torManager?.Stop(); } catch { }
            _ = RefreshTorIndicatorAsync();
            return;
        }

        if (_torManager == null)
        {
            _torManager = new TorManager();
            _torManager.StatusChanged += (_, _) => _ = RefreshTorIndicatorAsync();
        }

        var exe = TorManager.FindTorExe(_settings!.TorExePath);
        if (exe == null)
        {
            Ui(() =>
            {
                _torStatus.Text = "Tor ✗";
                _torStatus.ForeColor = Color.FromArgb(176, 32, 32);
                _torStatus.ToolTipText = T("tor.notFound");
            });
            SetStatus(T("status.torNotFound"));
            return;
        }

        TorProxy.Host = _settings.TorHost;
        TorProxy.Port = _settings.TorPort;

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<int>(pct => Ui(() =>
                {
                    _torStatus.Text = $"Tor {pct}%";
                    _torStatus.ForeColor = Color.FromArgb(180, 130, 40);
                }));
                await _torManager.StartAsync(exe, _settings.TorHost, _settings.TorPort, progress, ct)
                    .ConfigureAwait(false);
                SetStatus($"Tor ready at {_settings.TorHost}:{_settings.TorPort}.");
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer ApplyTorMode call.
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(T("status.torFailed"), ex.Message));
            }
            await RefreshTorIndicatorAsync().ConfigureAwait(false);
        }, ct);
    }

    private void ScheduleTimer_Tick(object? sender, EventArgs e)
    {
        if (_settings == null || _downloadManager == null) return;

        _ = RefreshTorIndicatorAsync();

        var now = DateTime.Now;

        if (_settings.ScheduleStartEnabled &&
            TimeSpan.TryParse(_settings.ScheduleStartTime, out var start) &&
            now.TimeOfDay >= start && _lastScheduleStart.Date != now.Date)
        {
            _lastScheduleStart = now;
            foreach (var task in _downloadManager.GetAllTasks())
            {
                if (task.Status is DownloadStatus.Pending or DownloadStatus.Paused
                    or DownloadStatus.Error or DownloadStatus.Cancelled)
                {
                    _ = _downloadManager.StartDownloadAsync(task.Id);
                }
            }
            SetStatus(string.Format(T("status.schedStart"), now));
        }

        if (_settings.ScheduleStopEnabled &&
            TimeSpan.TryParse(_settings.ScheduleStopTime, out var stop) &&
            now.TimeOfDay >= stop && _lastScheduleStop.Date != now.Date)
        {
            _lastScheduleStop = now;
            foreach (var task in _downloadManager.GetAllTasks())
            {
                _downloadManager.PauseDownload(task.Id);
            }
            SetStatus(string.Format(T("status.schedStop"), now));
        }
    }

    private void CheckAfterAllDone()
    {
        if (_afterActionFired || _settings == null || _downloadManager == null) return;
        if (string.Equals(_settings.ActionAfterAllDone, "None", StringComparison.OrdinalIgnoreCase)) return;

        var tasks = _downloadManager.GetAllTasks().ToList();
        if (tasks.Count == 0) return;
        if (tasks.Any(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Pending))
        {
            return;
        }

        _afterActionFired = true;
        RunAfterDoneAction(_settings.ActionAfterAllDone);
    }

    private void RunAfterDoneAction(string action)
    {
        try
        {
            if (string.Equals(action, "Shutdown", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start("shutdown", "/s /t 60 /c \"ifredrix: all downloads finished.\"");
                SetStatus(T("status.doneAll") + ". " + T("status.doneShutdownIn") + T("status.doneShutdownAbort"));
                Notify(T("status.doneAll"), T("status.doneShutdownIn"));
            }
            else if (string.Equals(action, "Hibernate", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start("shutdown", "/h");
                SetStatus(T("status.doneAll") + ". " + T("status.doneHibernate"));
            }
            else if (string.Equals(action, "Sleep", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                SetStatus(T("status.doneAll") + ". " + T("status.doneSleep"));
            }
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.doneFailed"), ex.Message));
        }
    }

    // ------------------------------------------------------------- control ----

    private void btnStart_Click(object sender, EventArgs e)
    {
        if (_selectedTask != null) _ = _downloadManager.StartDownloadAsync(_selectedTask.Id);
    }

    private void btnPause_Click(object sender, EventArgs e)
    {
        if (_selectedTask != null) _downloadManager.PauseDownload(_selectedTask.Id);
    }

    private void btnResume_Click(object sender, EventArgs e)
    {
        if (_selectedTask != null) _downloadManager.ResumeDownload(_selectedTask.Id);
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        if (_selectedTask == null) return;
        _downloadManager.CancelDownload(_selectedTask.Id);
        UpdateButtonStates();
    }

    private void btnRemove_Click(object sender, EventArgs e) => RemoveSelectedTask();

    /// <summary>Deletes the selected task: list row always, files only on
    /// explicit choice (asked only when files actually exist on disk).</summary>
    private void RemoveSelectedTask()
    {
        var task = _selectedTask;
        if (task == null || _downloadManager == null) return;
        var paths = _downloadManager.RemovalPaths(task.Id);
        var deleteFiles = false;
        if (paths.Count > 0)
        {
            using var dialog = new DeleteConfirmForm(task.FileName);
            if (dialog.ShowDialog(this) != DialogResult.OK ||
                dialog.Result == DeleteConfirmForm.Choice.Cancel)
            {
                return;
            }
            deleteFiles = dialog.Result == DeleteConfirmForm.Choice.ListAndFile;
        }
        _downloadManager.RemoveTask(task.Id);
        if (deleteFiles)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, true);
                }
                catch (Exception ex)
                {
                    SetStatus(string.Format(T("del.deleteFailed"), path, ex.Message));
                }
            }
        }
        _selectedTask = null;
        UpdateButtonStates();
    }

    private void btnOpenFolder_Click(object sender, EventArgs e)
    {
        var path = _selectedTask?.SavePath ?? _settings.DownloadPath;
        OpenInExplorer(path);
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void btnBrowsers_Click(object? sender, EventArgs e)
    {
        var targets = BrowserDetector.Detect(ExtensionRoot());
        using var dialog = new BrowserIntegrationForm(
            targets,
            () => _captureServer?.Heartbeats ??
                  (IReadOnlyDictionary<string, DateTime>)
                  new Dictionary<string, DateTime>());
        dialog.ShowDialog(this);
    }

    private static string ExtensionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "BrowserExtension");
            try
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            catch
            {
                // Keep walking up.
            }
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
    }

    private void OpenProperties()
    {
        if (_selectedTask == null || _downloadManager == null) return;
        using var dialog = new PropertiesForm(_downloadManager, _selectedTask.Id);
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// Opens (or focuses) the per-download progress window. It is a fully
    /// independent top-level window (own taskbar entry, no owner): it stays
    /// usable while the main window is minimized or covered.
    /// </summary>
    private void OpenProgress()
    {
        if (_selectedTask == null) return;
        OpenProgress(_selectedTask);
    }

    /// <summary>Opens (or focuses) the progress window for a given task.</summary>
    private void OpenProgress(DownloadTask task)
    {
        if (_downloadManager == null) return;
        if (_progressWindows.TryGetValue(task.Id, out var existing) && !existing.IsDisposed)
        {
            existing.Focus();
            return;
        }
        var window = new ProgressForm(_downloadManager, task.Id);
        var id = task.Id;
        window.FormClosed += (_, _) => _progressWindows.Remove(id);
        _progressWindows[task.Id] = window;
        window.Show();
    }

    /// <summary>Brings the main window back from tray life.</summary>
    private void RestoreFromTray()
    {
        Ui(() =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        });
    }

    /// <summary>Parks the window in the tray, keeping server+downloads alive.</summary>
    private void HideToTray()
    {
        Ui(() =>
        {
            Hide();
            ShowInTaskbar = false;
            if (!_trayHintShown && _tray != null)
            {
                _trayHintShown = true;
                _tray.BalloonTipTitle = T("lbl.tray");
                _tray.BalloonTipText = T("tray.minimized");
                _tray.ShowBalloonTip(3000);
            }
        });
    }

    private void OpenChecksum()
    {
        if (_selectedTask == null) return;
        var task = _selectedTask;
        if (task.Status != DownloadStatus.Completed || !File.Exists(task.SavePath))
        {
            SetStatus(T("hash.needFile"));
            return;
        }
        using var dialog = new ChecksumForm(task.FileName, task.SavePath);
        dialog.ShowDialog(this);
    }

    private void OpenSpeedLimit()
    {
        if (_selectedTask == null || _downloadManager == null) return;
        var task = _selectedTask;
        using var dialog = new SpeedLimitForm(task.FileName, task.SpeedLimitBps);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _downloadManager.SetTaskSpeedLimit(task.Id, dialog.BytesPerSecond);
        _downloadManager.SaveHistory(AppSettings.HistoryFile);
        SetStatus(dialog.BytesPerSecond > 0
            ? string.Format(T("status.limitSet"), task.FileName, dialog.BytesPerSecond / 1024)
            : string.Format(T("status.limitGlobal"), task.FileName));
    }

    private async Task ToggleTorRouteAsync()
    {
        if (_selectedTask == null || _downloadManager == null) return;
        var task = _selectedTask;
        var ok = await _downloadManager.SetUseTorAsync(task.Id, !task.UseTor);
        if (ok)
        {
            _downloadManager.SaveHistory(AppSettings.HistoryFile);
            SetStatus(string.Format(T(task.UseTor ? "status.torOn" : "status.torOff"), task.FileName));
            RefreshDataGridView();
        }
        UpdateButtonStates();
    }

    private async Task RefreshSelectedAsync()
    {
        if (_selectedTask == null || _downloadManager == null) return;
        SetStatus(T("status.refreshing"));
        try
        {
            await _downloadManager.RefreshTaskAsync(_selectedTask.Id);
            SetStatus(string.Format(T("status.refreshed"), _selectedTask.FileName));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("status.refreshFailed"), ex.Message));
        }
    }

    /// <summary>Links passed on the command line, queued once the UI is ready.</summary>
    public List<string> StartupUrls { get; } = new();

    private void SetupDragDrop()
    {
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        dataGridView.AllowDrop = true;
        dataGridView.DragEnter += OnDragEnter;
        dataGridView.DragDrop += OnDragDrop;
    }

    private static bool LooksDroppable(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(f =>
                    f.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))) return true;
        }
        if (data.GetDataPresent("UniformResourceLocator")) return true;
        if (data.GetDataPresent(DataFormats.Text))
        {
            var text = (string?)data.GetData(DataFormats.Text);
            if (!string.IsNullOrWhiteSpace(text) &&
                (text.Contains("://") || text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data != null && LooksDroppable(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var file in files.Where(f =>
                             f.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)))
                {
                    await AddAsync(file, DownloadType.Torrent);
                }
            }
        }

        string? text = null;
        if (e.Data.GetDataPresent("UniformResourceLocator"))
        {
            using var stream = (System.IO.Stream?)e.Data.GetData("UniformResourceLocator");
            if (stream != null)
            {
                using var reader = new System.IO.StreamReader(stream);
                text = (await reader.ReadToEndAsync()).TrimEnd('\0', '\r', '\n', ' ');
            }
        }
        text ??= (string?)e.Data.GetData(DataFormats.Text);

        foreach (var link in ExtractLinks(text ?? string.Empty))
        {
            await RouteLinkAsync(link);
        }
    }

    private void btnSettings_Click(object sender, EventArgs e)
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings();
            _settings.Save();
            SetStatus(T("status.settingsSaved"));
        }
        else
        {
            // Discard the live skin preview.
            ApplyTheme();
            ApplyToolbar();
        }
    }

    // ---------------------------------------------------------------- grid ----

    private void dataGridView_SelectionChanged(object sender, EventArgs e)
    {
        if (dataGridView.SelectedRows.Count == 0)
        {
            _selectedTask = null;
            UpdateButtonStates();
            return;
        }

        var id = dataGridView.SelectedRows[0].Cells["FileName"].Tag as string;
        _selectedTask = string.IsNullOrEmpty(id) ? null : _downloadManager?.GetTask(id);
        UpdateButtonStates();
    }

    private void dataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var id = dataGridView.Rows[e.RowIndex].Cells["FileName"].Tag as string;
        _selectedTask = string.IsNullOrEmpty(id) ? null : _downloadManager?.GetTask(id);
        UpdateButtonStates();
        OpenProgress();
    }

    private void cmbCategoryFilter_SelectedIndexChanged(object sender, EventArgs e) => RefreshDataGridView();

    private void txtSearch_TextChanged(object sender, EventArgs e) => RefreshDataGridView();

    // ------------------------------------------------------------- manager ----

    private void OnTaskAdded(object? sender, DownloadTask task) => Ui(() =>
    {
        _afterActionFired = false;
        RefreshDataGridView();
        UpdateStatusInfo();
        _downloadManager?.SaveHistory(AppSettings.HistoryFile);
    });

    private void OnTaskUpdated(object? sender, DownloadTask task) => Ui(() =>
    {
        UpdateDataGridViewRow(task);
    });

    private void OnTaskCompleted(object? sender, DownloadTask task) => Ui(() =>
    {
        UpdateDataGridViewRow(task);
        UpdateStatusInfo();
        SetStatus(string.Format(T("status.finished"), task.FileName));
        Notify(T("notify.finished"), task.FileName);
        _downloadManager?.SaveHistory(AppSettings.HistoryFile);
    });

    private void OnTaskFailed(object? sender, DownloadTask task) => Ui(() =>
    {
        UpdateDataGridViewRow(task);
        UpdateStatusInfo();
        SetStatus(string.Format(T("status.failedTask"), task.FileName, task.ErrorMessage));
        _downloadManager?.SaveHistory(AppSettings.HistoryFile);
    });

    private void OnTaskRemoved(object? sender, DownloadTask task) => Ui(() =>
    {
        RefreshDataGridView();
        UpdateButtonStates();
        UpdateStatusInfo();
        _downloadManager?.SaveHistory(AppSettings.HistoryFile);
    });

    private static readonly string[] CategoryKeys =
        { "All", "Video", "Audio", "Archive", "Document", "Application", "Image", "Torrent", "Other" };

    private string SelectedCategoryKey()
    {
        var index = cmbCategoryFilter.SelectedIndex;
        return index >= 0 && index < CategoryKeys.Length ? CategoryKeys[index] : "All";
    }

    private void RefreshDataGridView()
    {
        if (_downloadManager == null) return;
        var tasks = _downloadManager.GetAllTasks().ToList();
        var category = SelectedCategoryKey();
        var search = txtSearch.Text.Trim();

        var visible = tasks.Where(t =>
            (category == "All" || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || t.FileName.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();

        dataGridView.SuspendLayout();
        dataGridView.Rows.Clear();

        for (var i = 0; i < visible.Count; i++)
        {
            dataGridView.Rows.Add(BuildRow(visible[i], alt: i % 2 == 1));
        }

        dataGridView.ResumeLayout();
        lblDownloadCount.Text = $"Downloads: {tasks.Count}";
    }

    private DataGridViewRow BuildRow(DownloadTask task, bool alt)
    {
        var row = new DataGridViewRow();
        row.CreateCells(dataGridView);

        row.Cells[0].Value = task.FileName;
        row.Cells[0].Tag = task.Id;
        row.Cells[1].Value = task.SizeText;
        row.Cells[2].Value = task.ProgressPercentage;
        row.Cells[3].Value = task.FormattedSpeedDisplay;
        row.Cells[4].Value = task.FormattedTimeRemaining;
        row.Cells[5].Value = task.StatusText;
        row.Cells[6].Value = task.TypeText;
        row.Cells[7].Value = task.CategoryText;

        ApplyRowStyle(row, task, alt);
        return row;
    }

    private void UpdateDataGridViewRow(DownloadTask task)
    {
        foreach (DataGridViewRow row in dataGridView.Rows)
        {
            if (row.Cells[0].Tag as string != task.Id) continue;

            row.Cells[0].Value = task.FileName;
            row.Cells[1].Value = task.SizeText;
            row.Cells[2].Value = task.ProgressPercentage;
            row.Cells[3].Value = task.FormattedSpeedDisplay;
            row.Cells[4].Value = task.FormattedTimeRemaining;
            row.Cells[5].Value = task.StatusText;
            row.Cells[6].Value = task.TypeText;
            row.Cells[7].Value = task.CategoryText;

            ApplyRowStyle(row, task, row.Index % 2 == 1);
            break;
        }
    }

    /// <summary>
    /// Excel-style rows: zebra base (white/gray) with the status tint kept on
    /// the Status cell only, so glanceability survives the striping.
    /// </summary>
    private static void ApplyRowStyle(DataGridViewRow row, DownloadTask task, bool alt)
    {
        row.DefaultCellStyle.BackColor = alt ? Theme.GridAltRow : Theme.GridBack;
        row.DefaultCellStyle.ForeColor = Theme.Text;
        row.DefaultCellStyle.SelectionBackColor = Theme.SelectedRow;
        row.DefaultCellStyle.SelectionForeColor = Theme.SelectedRowText;

        var statusCell = row.Cells[5];
        statusCell.Style ??= new DataGridViewCellStyle();
        statusCell.Style.BackColor = Theme.StatusBackColor(task.Status);
        statusCell.Style.ForeColor = Theme.Text;
        statusCell.Style.SelectionBackColor = Theme.SelectedRow;
        statusCell.Style.SelectionForeColor = Theme.SelectedRowText;
    }

    // --------------------------------------------------------------- status ----

    private void UpdateButtonStates()
    {
        if (_selectedTask == null)
        {
            btnStart.Enabled = false;
            btnPause.Enabled = false;
            btnResume.Enabled = false;
            btnCancel.Enabled = false;
            btnRemove.Enabled = false;
            SyncDownloadMenu(false, false, false, false, false, false, false);
            if (_menuTorRoute != null)
            {
                _menuTorRoute.Checked = false;
                _menuTorRoute.Enabled = false;
            }
            return;
        }

        var status = _selectedTask.Status;
        btnStart.Enabled = status is DownloadStatus.Pending or DownloadStatus.Paused or DownloadStatus.Error or DownloadStatus.Cancelled;
        btnPause.Enabled = status is DownloadStatus.Downloading or DownloadStatus.Queued;
        btnResume.Enabled = status is DownloadStatus.Paused or DownloadStatus.Error;
        btnCancel.Enabled = status is DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Paused;
        btnRemove.Enabled = true;
        SyncDownloadMenu(btnStart.Enabled, btnPause.Enabled, btnResume.Enabled,
            btnCancel.Enabled, true, true, true);
        if (_menuTorRoute != null)
        {
            _menuTorRoute.Checked = _selectedTask?.UseTor == true;
            _menuTorRoute.Enabled = _selectedTask?.Type == DownloadType.Regular;
        }
    }

    private void SyncDownloadMenu(bool start, bool pause, bool resume, bool cancel,
        bool remove, bool folder, bool copy)
    {
        if (_menuStart != null) _menuStart.Enabled = start;
        if (_menuPause != null) _menuPause.Enabled = pause;
        if (_menuResume != null) _menuResume.Enabled = resume;
        if (_menuCancel != null) _menuCancel.Enabled = cancel;
        if (_menuRemove != null) _menuRemove.Enabled = remove;
        if (_menuOpenFolder != null) _menuOpenFolder.Enabled = folder;
        if (_menuCopyUrl != null) _menuCopyUrl.Enabled = copy;
        if (_rowTorRoute != null && _selectedTask != null)
        {
            _rowTorRoute.Checked = _selectedTask.UseTor;
            _rowTorRoute.Enabled = _selectedTask.Type == DownloadType.Regular;
        }
    }

    private void UpdateStatusInfo()
    {
        if (_downloadManager == null) return;

        var tasks = _downloadManager.GetAllTasks().ToList();
        var active = tasks.Count(t => t.Status == DownloadStatus.Downloading);
        var queued = tasks.Count(t => t.Status is DownloadStatus.Queued or DownloadStatus.Pending);

        // Streams report percent/s on this counter, not bytes: keep them out
        // of the byte total so the bar never mixes units.
        var down = tasks.Where(t => t.Status == DownloadStatus.Downloading && !t.IsPercentProgress).Sum(t => t.DownloadSpeed);
        var up = tasks.Where(t => t.Status == DownloadStatus.Downloading).Sum(t => t.UploadSpeed);

        toolStripStatusLabel2.Text = string.Format(T("bar.speed"), FormatSpeed(down), FormatSpeed(up));
        toolStripStatusLabel3.Text = string.Format(T("bar.active"), active, queued);

        var completed = tasks.Count(t => t.Status == DownloadStatus.Completed);
        lblDownloadCount.Text = string.Format(T("bar.counts"), tasks.Count, completed);

        CheckAfterAllDone();
    }

    private void SetStatus(string message)
    {
        Ui(() => lblStatus.Text = message);
    }

    private void Notify(string title, string message)
    {
        if (!_settings.ShowNotifications || _tray == null) return;
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = message;
            _tray.ShowBalloonTip(4000);
        }
        catch
        {
            // ignore
        }
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch
        {
            // ignore
        }
    }

    private void OnBrowserLinkCaptured(object? sender, LocalCaptureServer.CapturedLink link)
    {
        Ui(() =>
        {
            SetStatus(string.Format(T("notify.captured"), link.Source, link.Url));
            if (!link.Ui)
            {
                Notify(T("notify.capture"), $"{link.Source} -> {link.Url}");
                return;
            }
            // Picked in the browser panel: pop the progress window for it
            // (the window itself is the confirmation). Silent handoffs
            // keep the toast-only behaviour above.
            DownloadTask? found = null;
            var all = _downloadManager?.GetAllTasks();
            if (all != null)
            {
                foreach (var t in all)
                {
                    if (!string.Equals(t.Url, link.Url, StringComparison.OrdinalIgnoreCase)) continue;
                    if (found == null || t.CreatedAt > found.CreatedAt) found = t;
                }
            }
            if (found != null) OpenProgress(found);
            else Notify(T("notify.capture"), $"{link.Source} -> {link.Url}");
        });
    }

    private void UpdateCaptureIndicators()
    {
        if (_captureServer == null) return;
        ApplyIndicator(_captureChrome, "chrome", "Chrome");
        ApplyIndicator(_captureFirefox, "firefox", "Firefox");
        ApplyIndicator(_captureEdge, "edge", "Edge");
        ApplyIndicator(_captureOpera, "opera", "Opera");
    }

    private void ApplyIndicator(ToolStripStatusLabel label, string source, string display)
    {
        if (_captureServer!.Heartbeats.TryGetValue(source, out var t))
        {
            var age = DateTime.UtcNow - t.ToUniversalTime();
            if (age.TotalSeconds < 30)
            {
                label.Text = display + " ✓";
                label.ForeColor = Color.FromArgb(42, 122, 59);
                return;
            }
        }
        label.Text = display + " ✗";
        label.ForeColor = Color.FromArgb(140, 145, 155);
    }

    private static bool IsKnownStreamingHost(string url) => StreamCapture.IsStreamingUrl(url);

    private void btnStream_Click(object? sender, EventArgs e)
    {
        var url = txtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            SetStatus(T("status.streamUrlFirst"));
            return;
        }
        if (!IsKnownStreamingHost(url))
        {
            var ans = MessageBox.Show(this,
                T("msg.streamHostBody"),
                T("msg.streamHostTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;
        }
        txtUrl.Clear();
        OpenStreamDialog(url);
    }

        private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "0 KB/s";
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024L * 1024 * 1024) return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024.0 * 1024 * 1024):F1} GB/s";
    }
}

