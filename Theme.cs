using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Selectable skin palettes (Light/Dark + accent). Existing callers keep
/// reading <c>Theme.WindowBack</c> etc.; those now forward to
/// <see cref="Current"/> so a skin change propagates without touching
/// every form. Call <see cref="Apply"/> then <see cref="StyleForm"/>.
/// </summary>
public static class Theme
{
    public sealed class Palette
    {
        public Color WindowBack;
        public Color Surface;
        public Color SurfaceAlt;
        public Color Border;
        public Color BorderStrong;
        public Color Text;
        public Color TextMuted;
        public Color Accent;
        public Color AccentHover;
        public Color AccentSoft;
        public Color OnAccent;
        public Color Danger;
        public Color DangerSoft;
        public Color GridBack;
        public Color GridLine;
        public Color GridHeaderBack;
        public Color GridHeaderText;
        public Color GridAltRow;
        public Color SelectedRow;
        public Color SelectedRowText;
        public Color ProgressTrack;
        public Color ProgressFill;
        public Color StatusDownloading;
        public Color StatusCompleted;
        public Color StatusPaused;
        public Color StatusQueued;
        public Color StatusError;
        public Color StatusCancelled;
    }

    public static Palette Current { get; private set; } = Light(Color.FromArgb(0, 112, 214));
    public static string CurrentThemeName { get; private set; } = "Light";
    public static string CurrentAccentName { get; private set; } = "Blue";

    public static readonly IReadOnlyList<string> ThemeNames = new[] { "Light", "Dark" };
    public static readonly IReadOnlyList<string> AccentNames = new[] { "Blue", "Green", "Purple", "Orange" };

    private static Color AccentBase(string accent) => accent switch
    {
        "Green" => Color.FromArgb(46, 160, 67),
        "Purple" => Color.FromArgb(129, 92, 198),
        "Orange" => Color.FromArgb(214, 110, 0),
        _ => Color.FromArgb(0, 112, 214),
    };

    private static Palette Light(Color accent)
    {
        var hover = Darken(accent, 0.12f);
        return new Palette
        {
            WindowBack = Color.FromArgb(244, 246, 250),
            Surface = Color.White,
            SurfaceAlt = Color.FromArgb(249, 250, 253),
            Border = Color.FromArgb(206, 213, 224),
            BorderStrong = Color.FromArgb(176, 186, 202),
            Text = Color.FromArgb(28, 34, 44),
            TextMuted = Color.FromArgb(94, 104, 120),
            Accent = accent,
            AccentHover = hover,
            AccentSoft = Color.FromArgb(226, 239, 253),
            OnAccent = Color.White,
            Danger = Color.FromArgb(206, 55, 62),
            DangerSoft = Color.FromArgb(252, 226, 228),
            GridBack = Color.White,
            GridLine = Color.FromArgb(228, 233, 241),
            GridHeaderBack = Color.FromArgb(249, 250, 253),
            GridHeaderText = Color.FromArgb(78, 88, 105),
            GridAltRow = Color.FromArgb(250, 251, 253),
            SelectedRow = Color.FromArgb(214, 233, 253),
            SelectedRowText = Color.FromArgb(20, 26, 36),
            ProgressTrack = Color.FromArgb(228, 233, 241),
            ProgressFill = Color.FromArgb(0, 132, 224),
            StatusDownloading = Color.FromArgb(222, 238, 255),
            StatusCompleted = Color.FromArgb(216, 246, 224),
            StatusPaused = Color.FromArgb(255, 245, 216),
            StatusQueued = Color.FromArgb(238, 241, 246),
            StatusError = Color.FromArgb(253, 224, 226),
            StatusCancelled = Color.FromArgb(238, 241, 246),
        };
    }

    private static Palette Dark(Color accent)
    {
        var hover = Lighten(accent, 0.12f);
        return new Palette
        {
            WindowBack = Color.FromArgb(30, 34, 43),
            Surface = Color.FromArgb(40, 46, 57),
            SurfaceAlt = Color.FromArgb(35, 40, 50),
            Border = Color.FromArgb(62, 70, 86),
            BorderStrong = Color.FromArgb(92, 102, 122),
            Text = Color.FromArgb(231, 236, 243),
            TextMuted = Color.FromArgb(148, 158, 175),
            Accent = accent,
            AccentHover = hover,
            AccentSoft = Color.FromArgb(38, 62, 92),
            OnAccent = Color.White,
            Danger = Color.FromArgb(224, 108, 117),
            DangerSoft = Color.FromArgb(84, 36, 40),
            GridBack = Color.FromArgb(40, 46, 57),
            GridLine = Color.FromArgb(56, 63, 79),
            GridHeaderBack = Color.FromArgb(35, 40, 50),
            GridHeaderText = Color.FromArgb(160, 170, 188),
            GridAltRow = Color.FromArgb(37, 43, 54),
            SelectedRow = Color.FromArgb(48, 76, 110),
            SelectedRowText = Color.FromArgb(235, 240, 247),
            ProgressTrack = Color.FromArgb(56, 63, 79),
            ProgressFill = accent,
            StatusDownloading = Color.FromArgb(31, 62, 100),
            StatusCompleted = Color.FromArgb(32, 80, 52),
            StatusPaused = Color.FromArgb(92, 74, 30),
            StatusQueued = Color.FromArgb(55, 61, 76),
            StatusError = Color.FromArgb(96, 38, 42),
            StatusCancelled = Color.FromArgb(55, 61, 76),
        };
    }

    public static void Apply(string? themeName, string? accentName)
    {
        var theme = string.Equals(themeName, "Dark", System.StringComparison.OrdinalIgnoreCase)
            ? "Dark" : "Light";
        var accent = "Blue";
        foreach (var name in AccentNames)
        {
            if (string.Equals(name, accentName, System.StringComparison.OrdinalIgnoreCase))
            {
                accent = name;
                break;
            }
        }

        Current = theme == "Dark" ? Dark(AccentBase(accent)) : Light(AccentBase(accent));
        CurrentThemeName = theme;
        CurrentAccentName = accent;
    }

    // ------------------------------------------------------- forwarding ----
    public static Color WindowBack => Current.WindowBack;
    public static Color Surface => Current.Surface;
    public static Color SurfaceAlt => Current.SurfaceAlt;
    public static Color Border => Current.Border;
    public static Color BorderStrong => Current.BorderStrong;
    public static Color Text => Current.Text;
    public static Color TextMuted => Current.TextMuted;
    public static Color Accent => Current.Accent;
    public static Color AccentHover => Current.AccentHover;
    public static Color AccentSoft => Current.AccentSoft;
    public static Color OnAccent => Current.OnAccent;
    public static Color Danger => Current.Danger;
    public static Color DangerSoft => Current.DangerSoft;
    public static Color GridBack => Current.GridBack;
    public static Color GridLine => Current.GridLine;
    public static Color GridHeaderBack => Current.GridHeaderBack;
    public static Color GridHeaderText => Current.GridHeaderText;
    public static Color GridAltRow => Current.GridAltRow;
    public static Color SelectedRow => Current.SelectedRow;
    public static Color SelectedRowText => Current.SelectedRowText;
    public static Color ProgressTrack => Current.ProgressTrack;
    public static Color ProgressFill => Current.ProgressFill;
    public static Color StatusDownloading => Current.StatusDownloading;
    public static Color StatusCompleted => Current.StatusCompleted;
    public static Color StatusPaused => Current.StatusPaused;
    public static Color StatusQueued => Current.StatusQueued;
    public static Color StatusError => Current.StatusError;
    public static Color StatusCancelled => Current.StatusCancelled;

    /// <summary>Background tint used for a download row, based on its status.</summary>
    public static Color StatusBackColor(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading => StatusDownloading,
        DownloadStatus.Completed => StatusCompleted,
        DownloadStatus.Paused => StatusPaused,
        DownloadStatus.Queued => StatusQueued,
        DownloadStatus.Error => StatusError,
        DownloadStatus.Cancelled => StatusCancelled,
        _ => Surface
    };

    // ------------------------------------------------------------- icon ----

    /// <summary>The application icon (app.ico, embedded in the exe) applied to
    /// window title bars instead of the default WinForms glyph.</summary>
    public static Icon AppIcon { get; } = LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    // ------------------------------------------------------------ styling ----

    /// <summary>
    /// Recolors a form and its children for the current skin. Buttons tagged
    /// "primary" get the accent; windows get the application icon.
    /// </summary>
    public static void StyleForm(Control root)
    {
        // Windows get the application icon instead of the default WinForms glyph.
        // NB: Form.Icon never reads as null - WinForms returns its stock default
        // - so assign unconditionally; a null-guard silently kept the old
        // WinForms icon on every window.
        if (root is Form form) form.Icon = AppIcon;

        root.BackColor = WindowBack;
        root.ForeColor = Text;
        StyleChildren(root);
    }

    private static void StyleChildren(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button b:
                    if (Equals(b.Tag, "primary"))
                    {
                        b.BackColor = Accent;
                        b.ForeColor = OnAccent;
                        b.FlatAppearance.BorderSize = 0;
                    }
                    else
                    {
                        // Secondary button. Keep danger-tinted text (Cancel)
                        // readable on both skins.
                        var danger = b.ForeColor.ToArgb() == Danger.ToArgb() ||
                                     b.Text == "Cancel";
                        b.BackColor = Surface;
                        b.ForeColor = danger ? Danger : Text;
                        b.FlatAppearance.BorderColor = Border;
                    }
                    break;
                case CheckBox:
                    c.ForeColor = Text;
                    break;
                case TextBox t:
                    t.BackColor = Surface;
                    t.ForeColor = Text;
                    break;
                case ComboBox cb:
                    cb.BackColor = Surface;
                    cb.ForeColor = Text;
                    break;
                case ListView lv:
                    lv.BackColor = Surface;
                    lv.ForeColor = Text;
                    break;
                case CheckedListBox clb:
                    clb.BackColor = Surface;
                    clb.ForeColor = Text;
                    break;
                case ListBox lb:
                    lb.BackColor = Surface;
                    lb.ForeColor = Text;
                    break;
                case Label:
                    if (c.ForeColor != TextMuted)
                    {
                        c.ForeColor = Text;
                    }
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case StatusStrip ss:
                    ss.BackColor = SurfaceAlt;
                    ss.ForeColor = TextMuted;
                    break;
                case Panel:
                    c.BackColor = c.Dock == DockStyle.Top ? SurfaceAlt : WindowBack;
                    break;
                case DateTimePicker:
                    c.BackColor = Surface;
                    c.ForeColor = Text;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = Surface;
                    nud.ForeColor = Text;
                    break;
            }

            if (c is ToolStrip strip)
            {
                foreach (ToolStripItem item in strip.Items)
                {
                    item.ForeColor = TextMuted;
                }
            }

            if (c.HasChildren) StyleChildren(c);
        }
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = GridBack;
        grid.GridColor = Border;
        grid.DefaultCellStyle.BackColor = GridBack;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = SelectedRow;
        grid.DefaultCellStyle.SelectionForeColor = SelectedRowText;
        grid.AlternatingRowsDefaultCellStyle.BackColor = GridAltRow;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectedRow;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = SelectedRowText;
        grid.ColumnHeadersDefaultCellStyle.BackColor = GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = GridHeaderText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = GridHeaderText;
    }

    private static Color Darken(Color c, float amount)
    {
        return Color.FromArgb(c.A,
            (int)System.Math.Max(0, c.R * (1 - amount)),
            (int)System.Math.Max(0, c.G * (1 - amount)),
            (int)System.Math.Max(0, c.B * (1 - amount)));
    }

    private static Color Lighten(Color c, float amount)
    {
        return Color.FromArgb(c.A,
            (int)(c.R + (255 - c.R) * amount),
            (int)(c.G + (255 - c.G) * amount),
            (int)(c.B + (255 - c.B) * amount));
    }
}
