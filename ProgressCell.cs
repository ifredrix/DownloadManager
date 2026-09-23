using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>A DataGridView column that renders progress as a real bar.</summary>
public sealed class DataGridViewProgressColumn : DataGridViewColumn
{
    public DataGridViewProgressColumn()
        : base(new DataGridViewProgressCell())
    {
        Width = 160;
        MinimumWidth = 80;
    }

    public override DataGridViewCell CellTemplate
    {
        get => base.CellTemplate;
        set => base.CellTemplate = value as DataGridViewProgressCell
                                   ?? throw new InvalidCastException("Cell template must be a DataGridViewProgressCell.");
    }
}

public sealed class DataGridViewProgressCell : DataGridViewCell
{
    public override Type ValueType => typeof(double);
    public override object DefaultNewRowValue => 0d;

    protected override void Paint(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates cellState,
        object value,
        object formattedValue,
        string errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        var selected = (cellState & DataGridViewElementStates.Selected) != 0;

        using (var background = new SolidBrush(selected ? Theme.SelectedRow : cellStyle.BackColor))
        {
            graphics.FillRectangle(background, cellBounds);
        }

        base.PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

        var percent = ToPercent(value);
        var bar = new Rectangle(
            cellBounds.X + 8,
            cellBounds.Y + (cellBounds.Height - 12) / 2,
            Math.Max(12, cellBounds.Width - 16),
            12);

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var track = new SolidBrush(Theme.ProgressTrack))
        {
            graphics.FillRectangle(track, bar);
        }

        var fillWidth = (int)Math.Round(bar.Width * percent / 100.0);
        if (fillWidth > 0)
        {
            using var fill = new SolidBrush(Theme.ProgressFill);
            graphics.FillRectangle(fill, bar.X, bar.Y, fillWidth, bar.Height);
        }

        var text = percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        // Do NOT dispose: both fonts are shared (grid style / system default).
        var font = cellStyle.Font ?? Control.DefaultFont;
        var textSize = graphics.MeasureString(text, font);
        using var brush = new SolidBrush(Theme.Text);
        graphics.DrawString(
            text,
            font,
            brush,
            bar.X + (bar.Width - textSize.Width) / 2,
            bar.Y + (bar.Height - textSize.Height) / 2);
    }

    private static double ToPercent(object? value) => value switch
    {
        double d => Math.Clamp(d, 0, 100),
        float f => Math.Clamp(f, 0, 100),
        int i => Math.Clamp(i, 0, 100),
        long l => Math.Clamp(l, 0, 100),
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            => Math.Clamp(parsed, 0, 100),
        _ => 0
    };
}
