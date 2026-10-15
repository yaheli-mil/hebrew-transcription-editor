// Shared look for the installer, the launcher and the uninstaller: the editor's colours (gold on white),
// its font (Rubik, embedded), its logo, and a few hand-drawn controls. Written for the C# 5 compiler
// that ships with Windows (.NET Framework 4.x), so it builds with nothing installed.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyCompany("יהלי מילרמן")]
[assembly: AssemblyProduct("עורך תמלול")]
[assembly: AssemblyCopyright("© יהלי מילרמן")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Theme
{
    public const string Version = "1.0";
    public const string AppName = "עורך תמלול";
    public const string Credit = "יהלי מילרמן";
    public const string Instagram = "https://www.instagram.com/yali.millerman/";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HebrewTranscriptionEditor";

    public static readonly Color Bg = Color.White;
    public static readonly Color Raised = Color.FromArgb(245, 246, 249);
    public static readonly Color Ink = Color.FromArgb(20, 22, 28);
    public static readonly Color Ink2 = Color.FromArgb(74, 81, 98);
    public static readonly Color Ink3 = Color.FromArgb(139, 146, 163);
    public static readonly Color Line = Color.FromArgb(228, 230, 236);
    public static readonly Color Line2 = Color.FromArgb(212, 216, 225);
    public static readonly Color Accent = Color.FromArgb(140, 100, 16);
    public static readonly Color AccentHi = Color.FromArgb(230, 189, 85);
    public static readonly Color AccentLo = Color.FromArgb(184, 134, 28);
    public static readonly Color OnAccent = Color.FromArgb(36, 25, 0);
    public static readonly Color AccentSoft = Color.FromArgb(248, 240, 220);
    public static readonly Color Ok = Color.FromArgb(30, 138, 75);
    public static readonly Color OkSoft = Color.FromArgb(226, 245, 234);
    public static readonly Color Bad = Color.FromArgb(190, 37, 80);
    public static readonly Color BadSoft = Color.FromArgb(255, 228, 235);

    static PrivateFontCollection fonts;
    static FontFamily family;

    public static FontFamily Family
    {
        get { if (family == null) LoadFonts(); return family; }
    }

    static void LoadFonts()
    {
        try
        {
            fonts = new PrivateFontCollection();
            foreach (string name in new[] { "Rubik-Regular.ttf", "Rubik-Bold.ttf" })
            {
                byte[] data = Resource(name);
                IntPtr mem = Marshal.AllocCoTaskMem(data.Length);     // stays allocated: GDI+ reads it for as long as the font is used
                Marshal.Copy(data, 0, mem, data.Length);
                fonts.AddMemoryFont(mem, data.Length);
            }
            family = fonts.Families[0];
        }
        catch
        {
            family = new FontFamily("Segoe UI");
        }
    }

    public static byte[] Resource(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (MemoryStream m = new MemoryStream())
        {
            s.CopyTo(m);
            return m.ToArray();
        }
    }

    public static Icon AppIcon(int size)
    {
        try
        {
            using (MemoryStream m = new MemoryStream(Resource("app.ico")))
                return new Icon(m, size, size);
        }
        catch { return null; }
    }

    public static Font Font(float px, bool bold)
    {
        FontStyle st = bold && Family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        return new Font(Family, px, st, GraphicsUnit.Pixel);
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// The editor's logo: a gold rounded square with sound bars.
    public static void Logo(Graphics g, RectangleF r)
    {
        using (GraphicsPath p = Round(r, r.Width * 0.27f))
        using (LinearGradientBrush b = new LinearGradientBrush(r, AccentHi, AccentLo, 45f))
            g.FillPath(b, p);
        float box = r.Width * 0.52f, off = (r.Width - box) / 2, u = box / 24f;
        int[,] bars = { { 2, 10, 13 }, { 6, 6, 17 }, { 10, 3, 21 }, { 14, 8, 15 }, { 18, 5, 18 }, { 22, 10, 13 } };
        using (Pen pen = new Pen(OnAccent, u * 2.4f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            for (int i = 0; i < 6; i++)
            {
                float x = r.X + off + bars[i, 0] * u;
                g.DrawLine(pen, x, r.Y + off + bars[i, 1] * u, x, r.Y + off + bars[i, 2] * u);
            }
        }
    }

    /// A small camera glyph for the Instagram credit.
    public static void InstaGlyph(Graphics g, RectangleF r, Color c)
    {
        float u = r.Width / 24f;
        using (Pen pen = new Pen(c, Math.Max(1.2f, u * 2f)))
        {
            using (GraphicsPath p = Round(new RectangleF(r.X + 3 * u, r.Y + 3 * u, 18 * u, 18 * u), 5.5f * u))
                g.DrawPath(pen, p);
            g.DrawEllipse(pen, r.X + 8 * u, r.Y + 8 * u, 8 * u, 8 * u);
        }
        using (SolidBrush b = new SolidBrush(c))
            g.FillEllipse(b, r.X + 16.2f * u, r.Y + 5.4f * u, 2.4f * u, 2.4f * u);
    }

    /// Text set right to left: Near = the right edge.
    public static StringFormat Rtl(StringAlignment h, StringAlignment v)
    {
        StringFormat f = new StringFormat(StringFormatFlags.DirectionRightToLeft);
        f.Alignment = h;
        f.LineAlignment = v;
        f.Trimming = StringTrimming.EllipsisCharacter;
        return f;
    }

    public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
    {
        using (SolidBrush b = new SolidBrush(c))
        using (StringFormat sf = Rtl(h, v))
            g.DrawString(s, f, b, r, sf);
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static void Check(Graphics g, RectangleF r, Color c, float width)
    {
        using (Pen pen = new Pen(c, width))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new[] {
                new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.53f),
                new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
                new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.32f) });
        }
    }
}

/// A borderless window with the app's look: white, a soft shadow, rounded corners on Windows 11,
/// a close button at the top left (the end of a right-to-left window), draggable anywhere.
class ModernForm : Form
{
    protected readonly float K = 1f;
    bool closeHover;
    public bool CloseEnabled = true;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    public ModernForm(int width, int height)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        KeyPreview = true;
        RightToLeft = RightToLeft.Yes;
        using (Graphics g = CreateGraphics()) K = g.DpiX / 96f;
        ClientSize = new Size(D(width), D(height));
        Font = Theme.Font(D(14), false);
        Icon icon = Theme.AppIcon(32);
        if (icon != null) Icon = icon;
        Text = Theme.AppName;
    }

    public int D(float v) { return (int)Math.Round(v * K); }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;                 // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }   // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    protected Rectangle CloseBox { get { return new Rectangle(D(12), D(12), D(32), D(32)); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !(CloseEnabled && CloseBox.Contains(e.Location)))
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);    // drag the window
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool h = CloseEnabled && CloseBox.Contains(e.Location);
        if (h != closeHover) { closeHover = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(CloseBox); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (closeHover) { closeHover = false; Invalidate(CloseBox); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (CloseEnabled && e.Button == MouseButtons.Left && CloseBox.Contains(e.Location)) OnCloseClicked();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && CloseEnabled) OnCloseClicked();
    }

    protected virtual void OnCloseClicked() { Close(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (CloseEnabled)
        {
            Rectangle c = CloseBox;
            if (closeHover)
                using (GraphicsPath p = Theme.Round(c, D(8)))
                using (SolidBrush b = new SolidBrush(Theme.Raised))
                    g.FillPath(b, p);
            using (Pen pen = new Pen(closeHover ? Theme.Ink : Theme.Ink3, K * 1.6f))
            {
                float m = D(11);
                g.DrawLine(pen, c.X + m, c.Y + m, c.Right - m, c.Bottom - m);
                g.DrawLine(pen, c.Right - m, c.Y + m, c.X + m, c.Bottom - m);
            }
        }
        PaintContent(g);
        using (Pen pen = new Pen(Theme.Line))
            g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected virtual void PaintContent(Graphics g) { }

    /// The header every window shares: logo, name, a line under it.
    protected void PaintHeader(Graphics g, float top, float logo, string title, string subtitle)
    {
        float w = ClientSize.Width;
        Theme.Logo(g, new RectangleF((w - D(logo)) / 2, D(top), D(logo), D(logo)));
        using (Font f = Theme.Font(D(25), true))
            Theme.Text(g, title, f, Theme.Ink, new RectangleF(0, D(top + logo + 12), w, D(36)), StringAlignment.Center, StringAlignment.Near);
        if (subtitle != null)
            using (Font f = Theme.Font(D(14), false))
                Theme.Text(g, subtitle, f, Theme.Ink3, new RectangleF(0, D(top + logo + 48), w, D(22)), StringAlignment.Center, StringAlignment.Near);
    }

    public Bitmap Snapshot()
    {
        Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bmp, new Rectangle(Point.Empty, ClientSize));
        return bmp;
    }
}

/// A rounded button: gold (primary), red (danger) or outlined.
class Btn : Control
{
    public bool Primary, Danger;
    bool hover, down;
    readonly float k;

    public Btn(string text, bool primary, float k)
    {
        Text = text;
        Primary = primary;
        this.k = k;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
        base.OnKeyDown(e);
    }

    static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);
        RectangleF r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
        Color text;
        using (GraphicsPath p = Theme.Round(r, 11 * k))
        {
            if (Primary)
            {
                Color a = Theme.AccentHi, b = Theme.AccentLo;
                if (!Enabled) { a = Mix(a, Color.White, 0.5f); b = Mix(b, Color.White, 0.5f); }
                else if (down) { a = Mix(a, Color.Black, 0.06f); b = Mix(b, Color.Black, 0.06f); }
                else if (hover) { a = Mix(a, Color.White, 0.12f); b = Mix(b, Color.White, 0.12f); }
                using (LinearGradientBrush br = new LinearGradientBrush(r, a, b, 45f)) g.FillPath(br, p);
                text = Enabled ? Theme.OnAccent : Mix(Theme.OnAccent, Color.White, 0.45f);
            }
            else if (Danger)
            {
                Color c = hover ? Mix(Theme.Bad, Color.Black, 0.08f) : Theme.Bad;
                using (SolidBrush br = new SolidBrush(c)) g.FillPath(br, p);
                text = Color.White;
            }
            else
            {
                using (SolidBrush br = new SolidBrush(hover ? Theme.AccentSoft : Color.White)) g.FillPath(br, p);
                using (Pen pen = new Pen(hover ? Theme.AccentHi : Theme.Line2, k)) g.DrawPath(pen, p);
                text = Enabled ? Theme.Ink : Theme.Ink3;
            }
            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(Theme.Accent, 2 * k)) g.DrawPath(pen, p);
        }
        using (Font f = Theme.Font(15 * k, Primary || Danger))
            Theme.Text(g, Text, f, text, new RectangleF(0, 0, Width, Height), StringAlignment.Center, StringAlignment.Center);
    }
}

/// A check box with its label to its left (it sits at the right, where Hebrew lines start).
class CheckRow : Control
{
    public bool Checked;
    public event EventHandler Changed;
    readonly float k;
    bool hover;

    public CheckRow(string text, bool on, float k)
    {
        Text = text;
        Checked = on;
        this.k = k;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
        TabStop = true;
    }

    void Toggle()
    {
        Checked = !Checked;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e) { Toggle(); base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { e.Handled = true; Toggle(); } base.OnKeyDown(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        float s = 20 * k;
        RectangleF box = new RectangleF(Width - s - 2 * k, (Height - s) / 2, s, s);
        using (GraphicsPath p = Theme.Round(box, 6 * k))
        {
            if (Checked)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(box, Theme.AccentHi, Theme.AccentLo, 45f)) g.FillPath(b, p);
                Theme.Check(g, box, Theme.OnAccent, 2.2f * k);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (Pen pen = new Pen(hover ? Theme.AccentHi : Theme.Line2, 1.4f * k)) g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues) using (Pen pen = new Pen(Theme.Accent, 1.6f * k)) g.DrawPath(pen, p);
        }
        using (Font f = Theme.Font(14.5f * k, false))
            Theme.Text(g, Text, f, Theme.Ink, new RectangleF(0, 0, Width - s - 12 * k, Height), StringAlignment.Near, StringAlignment.Center);
    }
}

/// A text link (gold, underlined on hover).
class LinkText : Control
{
    readonly float k, px;
    bool hover;
    public string Url;

    public LinkText(string text, float px, float k)
    {
        Text = text;
        this.k = k;
        this.px = px;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { if (Url != null) Theme.OpenUrl(Url); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        using (Font f = new Font(Theme.Font(px * k, true), hover ? FontStyle.Bold | FontStyle.Underline : FontStyle.Bold))
            Theme.Text(g, Text, f, Theme.Accent, new RectangleF(0, 0, Width, Height), StringAlignment.Center, StringAlignment.Center);
    }
}

/// "נבנה על ידי יהלי מילרמן" with a camera glyph; opens the Instagram profile.
class CreditLink : Control
{
    readonly float k;
    bool hover;
    const string Prefix = "נבנה על ידי ";

    public CreditLink(float k)
    {
        this.k = k;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
        Size = new Size((int)(210 * k), (int)(26 * k));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Theme.OpenUrl(Theme.Instagram); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        using (Font reg = Theme.Font(13 * k, false))
        using (Font bold = new Font(Theme.Font(13 * k, true), hover ? FontStyle.Bold | FontStyle.Underline : FontStyle.Bold))
        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
        {
            sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.DirectionRightToLeft;
            float wPre = g.MeasureString(Prefix, reg, 1000, sf).Width;
            float wName = g.MeasureString(Theme.Credit, bold, 1000, sf).Width;
            float icon = 15 * k, gap = 6 * k;
            float x = Width - (Width - (icon + gap + wPre + wName)) / 2;   // centred, laid out from the right
            float cy = Height / 2f;
            Theme.InstaGlyph(g, new RectangleF(x - icon, cy - icon / 2, icon, icon), Theme.AccentLo);
            x -= icon + gap;
            using (SolidBrush b = new SolidBrush(Theme.Ink3))
                g.DrawString(Prefix, reg, b, new RectangleF(x - wPre, cy - reg.Height / 2f, wPre + 2, reg.Height + 2), sf);
            x -= wPre;
            using (SolidBrush b = new SolidBrush(hover ? Theme.Accent : Theme.Ink2))
                g.DrawString(Theme.Credit, bold, b, new RectangleF(x - wName, cy - bold.Height / 2f, wName + 2, bold.Height + 2), sf);
        }
    }
}
