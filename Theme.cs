using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexKVM;

/// <summary>Colours, fonts and drawing helpers shared by the whole UI.</summary>
internal static class Theme
{
    public static readonly Color Bg = Hex(0x0F1115);
    public static readonly Color Surface = Hex(0x171A21);
    public static readonly Color Field = Hex(0x0F1218);
    public static readonly Color Border = Hex(0x272C38);
    public static readonly Color BorderStrong = Hex(0x3A4252);
    public static readonly Color Text = Hex(0xE7EAF0);
    public static readonly Color Muted = Hex(0x8A92A3);
    public static readonly Color Accent = Hex(0x4F8CFF);
    public static readonly Color AccentHover = Hex(0x6A9FFF);
    public static readonly Color AccentDown = Hex(0x3E76E0);
    public static readonly Color Success = Hex(0x3ECF8E);
    public static readonly Color Warning = Hex(0xF5B94A);
    public static readonly Color Danger = Hex(0xE5596A);
    public static readonly Color DangerHover = Hex(0xEE6E7D);
    public static readonly Color DangerDown = Hex(0xCC4858);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Brand = new("Segoe UI Semibold", 19f);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 10.5f);

    private static Color Hex(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb));

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border = null, float borderWidth = 1f)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (border.HasValue) r = RectangleF.Inflate(r, -borderWidth / 2, -borderWidth / 2);
        using var path = RoundRect(r, radius);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (border.HasValue)
        {
            using var pen = new Pen(border.Value, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Dark title bar that matches the window background (Windows 11; dark grey on Windows 10).</summary>
    public static void ApplyWindowChrome(Form f)
    {
        int on = 1, caption = ColorRef(Bg), text = ColorRef(Text), border = ColorRef(Border);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_CAPTION_COLOR, ref caption, 4);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_TEXT_COLOR, ref text, 4);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_BORDER_COLOR, ref border, 4);
    }

    /// <summary>Gives a standard control dark scrollbars.</summary>
    public static void DarkScrollbars(Control c) => Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
