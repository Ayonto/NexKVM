using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexKVM;

/// <summary>
/// Screen arrangement editor, in the spirit of the Windows display settings: this PC sits in the
/// middle and the receiver is dragged (or clicked) onto the side where it physically stands.
/// That side is the edge the pointer crosses.
/// </summary>
internal sealed class ArrangementView : Control
{
    private static readonly TargetSide[] Sides = { TargetSide.Left, TargetSide.Right, TargetSide.Top, TargetSide.Bottom };

    private TargetSide _side = TargetSide.Left;
    private bool _dragging;
    private PointF _dragCentre;
    private TargetSide _dropSide;

    public event EventHandler? SideChanged;

    public ArrangementView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TargetSide Side
    {
        get => _side;
        set
        {
            if (value == _side) return;
            _side = value;
            Invalidate();
            SideChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- geometry ----

    private float S => DeviceDpi / 96f;
    private RectangleF Host
    {
        get { float w = 124 * S, h = 74 * S; return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h); }
    }

    private RectangleF Slot(TargetSide side)
    {
        var host = Host;
        float w = 96 * S, h = 56 * S, gap = 8 * S;
        float cx = host.X + host.Width / 2, cy = host.Y + host.Height / 2;
        return side switch
        {
            TargetSide.Left => new RectangleF(host.Left - gap - w, cy - h / 2, w, h),
            TargetSide.Right => new RectangleF(host.Right + gap, cy - h / 2, w, h),
            TargetSide.Top => new RectangleF(cx - w / 2, host.Top - gap - h, w, h),
            _ => new RectangleF(cx - w / 2, host.Bottom + gap, w, h),
        };
    }

    private TargetSide NearestSide(PointF p)
    {
        var host = Host;
        float dx = (p.X - (host.X + host.Width / 2)) / host.Width;
        float dy = (p.Y - (host.Y + host.Height / 2)) / host.Height;
        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx < 0 ? TargetSide.Left : TargetSide.Right)
            : (dy < 0 ? TargetSide.Top : TargetSide.Bottom);
    }

    // ---- interaction ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button != MouseButtons.Left) return;
        if (Slot(_side).Contains(e.Location))
        {
            _dragging = true;
            _dragCentre = e.Location;
            _dropSide = _side;
            Capture = true;
            Invalidate();
            return;
        }
        foreach (var s in Sides)
            if (Slot(s).Contains(e.Location)) { Side = s; return; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragCentre = new PointF(Math.Clamp(e.X, 0, Width), Math.Clamp(e.Y, 0, Height));
            _dropSide = NearestSide(_dragCentre);
            Invalidate();
            return;
        }
        bool overAny = false;
        foreach (var s in Sides) overAny |= Slot(s).Contains(e.Location);
        Cursor = Slot(_side).Contains(e.Location) ? Cursors.SizeAll : overAny ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        Invalidate();
        Side = _dropSide;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: Side = TargetSide.Left; break;
            case Keys.Right: Side = TargetSide.Right; break;
            case Keys.Up: Side = TargetSide.Top; break;
            case Keys.Down: Side = TargetSide.Bottom; break;
        }
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    // ---- painting ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color back = Parent?.BackColor ?? Theme.Surface;
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = S, radius = 8 * s;
        Color accent = Enabled ? Theme.Accent : Theme.BorderStrong;
        TargetSide shown = _dragging ? _dropSide : _side;

        // Empty positions the receiver could go to.
        foreach (var side in Sides)
        {
            if (side == shown) continue;
            using var path = Theme.RoundRect(RectangleF.Inflate(Slot(side), -0.5f, -0.5f), radius);
            using var pen = new Pen(Theme.Border, 1f) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        // This PC.
        var host = Host;
        Theme.FillRound(g, host, radius, Theme.Field, Theme.BorderStrong, 1.5f * s);
        DrawLabel(g, host, "This PC", Enabled ? Theme.Text : Theme.Muted);

        // The edge the pointer crosses.
        using (var edge = new Pen(accent, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float inset = 14 * s;
            switch (shown)
            {
                case TargetSide.Left: g.DrawLine(edge, host.Left, host.Top + inset, host.Left, host.Bottom - inset); break;
                case TargetSide.Right: g.DrawLine(edge, host.Right, host.Top + inset, host.Right, host.Bottom - inset); break;
                case TargetSide.Top: g.DrawLine(edge, host.Left + inset, host.Top, host.Right - inset, host.Top); break;
                default: g.DrawLine(edge, host.Left + inset, host.Bottom, host.Right - inset, host.Bottom); break;
            }
        }

        // The receiver: docked, or following the pointer with a preview of where it will land.
        var docked = Slot(shown);
        if (_dragging)
        {
            using var path = Theme.RoundRect(RectangleF.Inflate(docked, -0.5f, -0.5f), radius);
            using var pen = new Pen(accent, 1.5f * s) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
            var r = new RectangleF(_dragCentre.X - docked.Width / 2, _dragCentre.Y - docked.Height / 2, docked.Width, docked.Height);
            Theme.FillRound(g, r, radius, Theme.Blend(back, accent, 0.45f), accent, 1.5f * s);
            DrawLabel(g, r, "Receiver", Color.White);
        }
        else
        {
            Theme.FillRound(g, docked, radius, Theme.Blend(back, accent, 0.22f), accent, 1.5f * s);
            DrawLabel(g, docked, "Receiver", Enabled ? Theme.Text : Theme.Muted);
        }
    }

    private static void DrawLabel(Graphics g, RectangleF r, string text, Color color) =>
        TextRenderer.DrawText(g, text, Theme.Strong, Rectangle.Round(r), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
}
