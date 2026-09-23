using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>
/// Circular "traffic light" button styled after macOS title-bar controls.
/// Always renders a perfect circle: the control forces itself square and the
/// disc is painted centered on the smaller dimension, so DPI scaling can
/// never stretch it into an oval. Click behaviour is inherited from Button.
/// </summary>
public class TrafficLightButton : Button
{
    public enum LightKind { Close, Minimize, Maximize }

    private LightKind _kind = LightKind.Close;
    private Color _baseColor = Color.FromArgb(230, 106, 100);
    private Color _ringColor = Color.FromArgb(190, 70, 65);
    private bool _hovering;

    public LightKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    /// <summary>Inner disc colour; the outer ring is computed automatically.</summary>
    public Color LightColor
    {
        get => _baseColor;
        set
        {
            _baseColor = value;
            _ringColor = Darken(value, 0.32f);
            Invalidate();
        }
    }

    public TrafficLightButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(16, 16);
        MinimumSize = new Size(10, 10);
        ClipToCircle();
    }

    /// <summary>Never allow a non-square size, the source of oval rendering.</summary>
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        var side = System.Math.Max(MinimumSize.Width, System.Math.Min(width, height));
        base.SetBoundsCore(x, y, side, side, specified);
    }

    private void ClipToCircle()
    {
        var side = System.Math.Min(Width, Height);
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, side, side);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Paint over the parent so the button blends with the title bar.
        if (Parent != null) g.Clear(Parent.BackColor);

        var side = (float)System.Math.Min(Width, Height);
        var cx = Width / 2f;
        var cy = Height / 2f;
        var outerR = side / 2f - 0.5f;
        var innerR = outerR - 1.5f;

        // Outer darker ring.
        using (var ringPen = new Pen(_ringColor, 1.5f))
            g.DrawEllipse(ringPen, cx - outerR, cy - outerR, outerR * 2, outerR * 2);

        // Main disc - dimmed when not hovered so the three feel "off".
        var discColor = _hovering
            ? _baseColor
            : Color.FromArgb(200,
                (int)(_baseColor.R * 0.85f),
                (int)(_baseColor.G * 0.85f),
                (int)(_baseColor.B * 0.85f));
        using (var mainBrush = new SolidBrush(discColor))
            g.FillEllipse(mainBrush, cx - innerR, cy - innerR, innerR * 2, innerR * 2);

        // Subtle top gloss to give it that 3D feel.
        var glossH = innerR;
        using (var glossPath = new GraphicsPath())
        {
            glossPath.AddEllipse(cx - innerR, cy - innerR, innerR * 2, glossH);
            using var glossBrush = new PathGradientBrush(glossPath)
            {
                CenterColor = Color.FromArgb(110, 255, 255, 255)
            };
            glossBrush.SurroundColors = new[] { Color.Transparent };
            g.FillPath(glossBrush, glossPath);
        }

        // Symbol appears only when the cursor is over the button.
        if (_hovering)
        {
            using var pen = new Pen(Color.FromArgb(150, 40, 0, 0), 1.3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            switch (_kind)
            {
                case LightKind.Close:
                    g.DrawLine(pen, cx - 3, cy - 3, cx + 3, cy + 3);
                    g.DrawLine(pen, cx + 3, cy - 3, cx - 3, cy + 3);
                    break;
                case LightKind.Minimize:
                    g.DrawLine(pen, cx - 3.5f, cy, cx + 3.5f, cy);
                    break;
                case LightKind.Maximize:
                    g.DrawLine(pen, cx - 3.5f, cy, cx + 3.5f, cy);
                    g.DrawLine(pen, cx, cy - 3.5f, cx, cy + 3.5f);
                    break;
            }
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovering = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClipToCircle();
    }

    private static Color Darken(Color c, float amount)
    {
        return Color.FromArgb(c.A,
            (int)System.Math.Max(0, c.R * (1 - amount)),
            (int)System.Math.Max(0, c.G * (1 - amount)),
            (int)System.Math.Max(0, c.B * (1 - amount)));
    }
}
