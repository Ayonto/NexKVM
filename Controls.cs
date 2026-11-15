using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexKVM;

/// <summary>A rounded surface with a title; the building block of the window.</summary>
internal sealed class Card : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.FillRound(e.Graphics, ClientRectangle, 10 * s, Theme.Surface, Theme.Border);
        TextRenderer.DrawText(e.Graphics, Title, Theme.CardTitle,
            new Point((int)(19 * s), (int)(14 * s)), Theme.Text, TextFormatFlags.NoPadding);
    }
}

/// <summary>Two-or-more option switch drawn as one pill.</summary>
internal sealed class SegmentedControl : Control
{
    private readonly string[] _items;
    private int _selected, _hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedControl(params string[] items)
    {
        _items = items;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _items.Length) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int IndexAt(int x) => Math.Clamp(x * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != _hover) { _hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); SelectedIndex = IndexAt(e.X); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        else if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.FillRound(g, ClientRectangle, 8 * s, Theme.Field, Focused ? Theme.BorderStrong : Theme.Border);

        float pad = 4 * s, w = (Width - pad * 2) / _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new RectangleF(pad + i * w, pad, w, Height - pad * 2);
            bool sel = i == _selected;
            if (sel) Theme.FillRound(g, r, 6 * s, Enabled ? Theme.Accent : Theme.BorderStrong);
            else if (i == _hover && Enabled) Theme.FillRound(g, r, 6 * s, Theme.Surface);
            Color fg = sel ? Color.White : Enabled ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, _items[i], sel ? Theme.Strong : Theme.Body, Rectangle.Round(r), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

internal enum ButtonKind { Primary, Danger, Ghost }

/// <summary>Flat rounded button with hover and pressed states.</summary>
internal sealed class AccentButton : Button
{
    private bool _hover, _down;
    private ButtonKind _kind = ButtonKind.Primary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    public AccentButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        Font = Theme.ButtonFont;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);

        Color fill, fg = Color.White;
        Color? border = null;
        switch (_kind)
        {
            case ButtonKind.Danger:
                fill = _down ? Theme.DangerDown : _hover ? Theme.DangerHover : Theme.Danger; break;
            case ButtonKind.Ghost:
                fill = _down ? Theme.Border : _hover ? Theme.Blend(Theme.Field, Theme.Border, 0.6f) : Theme.Field;
                border = Theme.Border; fg = Theme.Text; break;
            default:
                fill = _down ? Theme.AccentDown : _hover ? Theme.AccentHover : Theme.Accent; break;
        }
        if (!Enabled) { fill = Theme.Border; fg = Theme.Muted; border = null; }

        Theme.FillRound(g, ClientRectangle, 8 * s, fill, border);
        if (Focused && ShowFocusCues)
            Theme.FillRound(g, RectangleF.Inflate(ClientRectangle, -3 * s, -3 * s), 6 * s, Color.Transparent, Color.FromArgb(120, Color.White));
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>On/off switch with a label.</summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        float tw = 38 * s, th = 22 * s, ty = (Height - th) / 2f;
        var track = new RectangleF(0.5f, ty, tw, th);
        Color on = Enabled ? Theme.Accent : Theme.BorderStrong;
        Theme.FillRound(g, track, th / 2, Checked ? on : Theme.Field, Checked ? on : Theme.BorderStrong);
        float kd = th - 8 * s, kx = Checked ? track.Right - kd - 4 * s : track.X + 4 * s;
        using (var b = new SolidBrush(Checked ? Color.White : Theme.Muted))
            g.FillEllipse(b, kx, ty + 4 * s, kd, kd);

        var textRect = new Rectangle((int)(tw + 12 * s), 0, Width - (int)(tw + 12 * s), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Single-line text field with a rounded border that lights up on focus.</summary>
internal sealed class InputBox : Panel
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.Body
    };

    public InputBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Field;
        Cursor = Cursors.IBeam;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Click += (_, _) => Box.Focus();
    }

    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = (int)(12 * DeviceDpi / 96f);
        Box.SetBounds(pad, (Height - Box.Height) / 2, Width - pad * 2, Box.Height);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Box.ForeColor = Enabled ? Theme.Text : Theme.Muted;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.FillRound(e.Graphics, ClientRectangle, 8 * s, Theme.Field,
            Box.Focused && !Box.ReadOnly ? Theme.Accent : Theme.Border);
    }
}

/// <summary>Small rounded badge: a coloured dot and a status phrase.</summary>
internal sealed class StatusPill : Control
{
    private Color _color = Theme.Muted;

    public StatusPill()
    {
        DoubleBuffered = true;
        Font = Theme.Strong;
    }

    /// <summary>Updates the badge and resizes it to fit, keeping its right edge fixed.</summary>
    public void Set(string text, Color color)
    {
        float s = DeviceDpi / 96f;
        int right = Right;
        Text = text;
        _color = color;
        Width = TextRenderer.MeasureText(text, Font).Width + (int)(38 * s);
        Left = right - Width;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        Color back = Parent?.BackColor ?? Theme.Bg;
        g.Clear(back);
        Theme.FillRound(g, ClientRectangle, Height / 2f, Theme.Blend(back, _color, 0.14f), Theme.Blend(back, _color, 0.35f));
        float d = 8 * s;
        using (var b = new SolidBrush(_color)) g.FillEllipse(b, 13 * s, (Height - d) / 2f, d, d);
        var r = new Rectangle((int)(27 * s), 0, Width - (int)(27 * s), Height);
        TextRenderer.DrawText(g, Text, Font, r, _color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
