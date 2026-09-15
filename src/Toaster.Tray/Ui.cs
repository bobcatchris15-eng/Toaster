using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

// Visual language for the Toaster console: dark blue-grey matte glass, bold
// letter-spaced display type for labels, near-monospace type for readouts.
static class Theme
{
    public static readonly Color BgTop = Color.FromArgb(0x1B, 0x26, 0x33);
    public static readonly Color BgBottom = Color.FromArgb(0x0A, 0x10, 0x16);
    public static readonly Color Surface = Color.FromArgb(0x16, 0x21, 0x2D);
    public static readonly Color SurfaceHover = Color.FromArgb(0x1F, 0x2D, 0x3B);
    public static readonly Color SurfaceSunken = Color.FromArgb(0x10, 0x18, 0x21);
    public static readonly Color Border = Color.FromArgb(0x2A, 0x3A, 0x4A);
    public static readonly Color BorderHi = Color.FromArgb(0x41, 0x57, 0x6B);
    public static readonly Color Text = Color.FromArgb(0xE9, 0xF0, 0xF6);
    public static readonly Color Muted = Color.FromArgb(0x7C, 0x91, 0xA6);
    public static readonly Color Faint = Color.FromArgb(0x53, 0x66, 0x79);
    public static readonly Color Accent = Color.FromArgb(0xFF, 0x9A, 0x3C);
    public static readonly Color Cyan = Color.FromArgb(0x55, 0xC6, 0xE8);
    public static readonly Color Violet = Color.FromArgb(0xA4, 0x92, 0xF5);
    public static readonly Color Ok = Color.FromArgb(0x49, 0xD9, 0x91);
    public static readonly Color Bad = Color.FromArgb(0xF3, 0x6F, 0x6F);

    public static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI", "Tahoma");
    public static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

    private static string Pick(params string[] candidates)
    {
        using var installed = new InstalledFontCollection();
        var available = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in candidates)
            if (available.Contains(name)) return name;
        return candidates[^1];
    }

    /// <summary>NoPrefix matters: labels such as "Manuals &amp; Sources" must not be read as mnemonics.</summary>
    public const TextFormatFlags Flat = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    public static Font Display(float size, FontStyle style = FontStyle.Bold) => new(DisplayFamily, size, style, GraphicsUnit.Point);
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => new(MonoFamily, size, style, GraphicsUnit.Point);

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Draws (or just measures) text with manual letter spacing. Returns the drawn width.</summary>
    public static int Tracked(Graphics g, string text, Font font, Color color, int x, int y, int tracking, bool measureOnly = false)
    {
        var start = x;
        foreach (var ch in text)
        {
            if (ch == ' ') { x += (int)(font.Size * 0.85f) + tracking; continue; }
            var s = ch.ToString();
            var w = TextRenderer.MeasureText(g, s, font, new Size(int.MaxValue, int.MaxValue), Theme.Flat).Width;
            if (!measureOnly) TextRenderer.DrawText(g, s, font, new Point(x, y), color, Theme.Flat);
            x += w + tracking;
        }
        return Math.Max(0, x - start - tracking);
    }

    private static Bitmap? _noise;

    /// <summary>Fine static grain, tiled. This is what stops the gradient reading as flat plastic.</summary>
    private static Bitmap Noise()
    {
        if (_noise is not null) return _noise;
        var bmp = new Bitmap(96, 96);
        var rng = new Random(20260915);
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var v = rng.Next(0, 3) == 0 ? rng.Next(0, 10) : 0;
                bmp.SetPixel(x, y, Color.FromArgb(v, 255, 255, 255));
            }
        return _noise = bmp;
    }

    /// <summary>Matte glass: cool diagonal gradient, a soft top-left bloom, grain, and a vignette.</summary>
    public static void PaintGlass(Graphics g, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var bg = new LinearGradientBrush(r, BgTop, BgBottom, 105f))
            g.FillRectangle(bg, r);

        var bloom = new Rectangle(r.X - r.Width / 3, r.Y - r.Height, r.Width * 3 / 2, r.Height * 2);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(bloom);
            using var bloomBrush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(26, 0x7F, 0xB4, 0xE6),
                SurroundColors = new[] { Color.FromArgb(0, 0x7F, 0xB4, 0xE6) },
                CenterPoint = new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.06f)
            };
            g.FillRectangle(bloomBrush, r);
        }

        using (var grain = new TextureBrush(Noise(), WrapMode.Tile))
            g.FillRectangle(grain, r);

        using (var vignette = new GraphicsPath())
        {
            vignette.AddEllipse(new Rectangle(r.X - r.Width / 4, r.Y - r.Height / 4, r.Width * 3 / 2, r.Height * 3 / 2));
            using var vb = new PathGradientBrush(vignette)
            {
                CenterColor = Color.FromArgb(0, 0, 0, 0),
                SurroundColors = new[] { Color.FromArgb(70, 0, 0, 0) }
            };
            g.FillRectangle(vb, r);
        }
    }

    /// <summary>
    /// Repaints the ancestor glass beneath a user-painted control. WinForms' transparent
    /// BackColor is unreliable once ControlStyles.UserPaint is set, so we redraw the
    /// procedural background ourselves, translated into the child's coordinate space.
    /// </summary>
    public static void PaintBackdrop(Control control, Graphics g)
    {
        Control? root = control.Parent;
        while (root is not null and not GlassPanel) root = root.Parent;
        if (root is null) { g.Clear(BgBottom); return; }

        var here = control.PointToScreen(Point.Empty);
        var there = root.PointToScreen(Point.Empty);
        var state = g.Save();
        g.TranslateTransform(there.X - here.X, there.Y - here.Y);
        PaintGlass(g, root.ClientRectangle);
        g.Restore(state);
    }
}

/// <summary>
/// Opts the process into the shell's dark mode so common-control non-client parts —
/// scrollbars above all — stop rendering light against our dark surfaces. These uxtheme
/// entry points are ordinal-only and undocumented, so every call is best-effort.
/// </summary>
static class DarkMode
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int mode);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    public static void Enable()
    {
        try
        {
            SetPreferredAppMode(2); // ForceDark
            FlushMenuThemes();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}

/// <summary>A read-only console readout whose non-client scrollbar follows the dark theme.</summary>
sealed class ConsoleBox : TextBox
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr handle, string? appName, string? idList);

    public ConsoleBox()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Muted;
        Font = Theme.Mono(8.5f);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Win11 themes the scrollbar dark for windows marked DarkMode_Explorer.
        try { SetWindowTheme(Handle, "DarkMode_Explorer", null); }
        catch (DllNotFoundException) { /* cosmetic only */ }
        catch (EntryPointNotFoundException) { }
    }
}

/// <summary>Root background. Everything else sits on this, transparent.</summary>
sealed class GlassPanel : Panel
{
    public GlassPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.BgBottom;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintGlass(e.Graphics, ClientRectangle);
}

/// <summary>A raised rounded surface with a hairline border.</summary>
class CardPanel : Panel
{
    public int Radius { get; set; } = 8;
    public Color Fill { get; set; } = Theme.Surface;
    public Color Edge { get; set; } = Theme.Border;

    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
        Padding = new Padding(14, 11, 14, 11);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = Theme.Round(r, Radius);
        using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
        using var pen = new Pen(Edge);
        g.DrawPath(pen, path);
    }
}

/// <summary>Big-number readout tile: rule, tracked caption, monospace value.</summary>
sealed class MetricTile : Control
{
    private string _value = "—";
    private bool _hot;

    public string Caption { get; set; } = "";
    public Color Rule { get; set; } = Theme.Accent;
    public string Suffix { get; set; } = "";

    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    public MetricTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width <= 2 || r.Height <= 2) return;

        using (var path = Theme.Round(r, 8))
        {
            using (var b = new SolidBrush(_hot ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, path);
            using var pen = new Pen(_hot ? Theme.BorderHi : Theme.Border);
            g.DrawPath(pen, path);
        }

        // Accent rule across the top edge — the tile's identity colour.
        using (var rule = new SolidBrush(Rule))
            g.FillRectangle(rule, new Rectangle(13, 0, Math.Min(34, r.Width - 26), 2));

        using var caption = Theme.Display(7.5f);
        Theme.Tracked(g, Caption.ToUpperInvariant(), caption, _hot ? Theme.Muted : Theme.Faint, 13, 16, 2);

        using var value = Theme.Mono(25f, FontStyle.Bold);
        var valueSize = TextRenderer.MeasureText(g, _value, value, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        var baseline = r.Bottom - valueSize.Height - 13;
        TextRenderer.DrawText(g, _value, value, new Point(12, baseline), Theme.Text, Theme.Flat);

        if (Suffix.Length > 0)
        {
            using var suffix = Theme.Display(7.5f);
            Theme.Tracked(g, Suffix.ToUpperInvariant(), suffix, Theme.Faint,
                14 + valueSize.Width, baseline + valueSize.Height - 12, 1);
        }
    }
}

/// <summary>Status pill: pulse dot plus a tracked state word.</summary>
sealed class StatusPill : Control
{
    private string _state = "CHECKING";
    private Color _tone = Theme.Muted;

    public string Detail { get; set; } = "";

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    public void Set(string state, Color tone, string detail)
    {
        _state = state.ToUpperInvariant();
        _tone = tone;
        Detail = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width <= 2 || r.Height <= 2) return;

        using (var path = Theme.Round(r, r.Height / 2))
        {
            using (var b = new SolidBrush(Theme.SurfaceSunken)) g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(120, _tone));
            g.DrawPath(pen, path);
        }

        var cy = r.Height / 2;
        using (var halo = new SolidBrush(Color.FromArgb(55, _tone)))
            g.FillEllipse(halo, 13, cy - 7, 14, 14);
        using (var dot = new SolidBrush(_tone))
            g.FillEllipse(dot, 17, cy - 3, 6, 6);

        using var state = Theme.Display(8f);
        var end = Theme.Tracked(g, _state, state, _tone, 34, cy - 7, 2);

        if (Detail.Length > 0)
        {
            using var detail = Theme.Mono(8f);
            TextRenderer.DrawText(g, Detail, detail, new Point(34 + end + 12, cy - 7), Theme.Faint, Theme.Flat);
        }
    }
}

/// <summary>Top navigation: tracked uppercase labels with an accent underline.</summary>
sealed class NavStrip : Control
{
    private readonly List<string> _items = new();
    private readonly List<Rectangle> _hits = new();
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedChanged;

    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public NavStrip(params string[] items)
    {
        _items.AddRange(items);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _hits.FindIndex(h => h.Contains(e.Location));
        if (hit != _hover) { _hover = hit; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit = _hits.FindIndex(h => h.Contains(e.Location));
        if (hit >= 0) SelectedIndex = hit;
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hits.Clear();

        using (var baseline = new SolidBrush(Theme.Border))
            g.FillRectangle(baseline, new Rectangle(0, Height - 1, Width, 1));

        using var font = Theme.Display(8.5f);
        var x = 2;
        for (var i = 0; i < _items.Count; i++)
        {
            var label = _items[i].ToUpperInvariant();
            var w = Theme.Tracked(g, label, font, Theme.Text, 0, 0, 2, measureOnly: true);
            var hit = new Rectangle(x, 0, w + 28, Height);
            _hits.Add(hit);

            var selected = i == _selected;
            var color = selected ? Theme.Text : _hover == i ? Theme.Muted : Theme.Faint;
            Theme.Tracked(g, label, font, color, x + 14, (Height - 14) / 2 - 1, 2);

            if (selected)
                using (var underline = new SolidBrush(Theme.Accent))
                    g.FillRectangle(underline, new Rectangle(x + 14, Height - 2, w, 2));

            x += w + 28;
        }
    }
}

/// <summary>Flat command button. Primary variant is a filled accent slab.</summary>
sealed class FlatButton : Button
{
    private bool _hot, _down;

    public bool Primary { get; set; }
    public int Tracking { get; set; } = 2;

    public FlatButton(string text, bool primary = false)
    {
        Primary = primary;
        Text = text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width <= 2 || r.Height <= 2) return;

        Color fill, edge, ink;
        if (Primary)
        {
            fill = _down ? Color.FromArgb(0xD6, 0x7C, 0x27) : _hot ? Color.FromArgb(0xFF, 0xAA, 0x55) : Theme.Accent;
            edge = fill;
            ink = Color.FromArgb(0x16, 0x10, 0x06);
        }
        else
        {
            fill = _down ? Theme.SurfaceSunken : _hot ? Theme.SurfaceHover : Theme.Surface;
            edge = _hot ? Theme.BorderHi : Theme.Border;
            ink = _hot ? Theme.Text : Theme.Muted;
        }

        using (var path = Theme.Round(r, 7))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using var pen = new Pen(edge);
            g.DrawPath(pen, path);
        }

        // Fit the label rather than letting it spill past the slab: give up the
        // letter spacing first, then step the type size down.
        var label = Text.ToUpperInvariant();
        var available = Width - 20;
        var tracking = Tracking;
        var size = 8.5f;
        Font font;
        int w;
        while (true)
        {
            font = Theme.Display(size);
            w = Theme.Tracked(g, label, font, ink, 0, 0, tracking, measureOnly: true);
            if (w <= available || (tracking == 0 && size <= 6.5f)) break;
            font.Dispose();
            if (tracking > 0) tracking--;
            else size -= 0.5f;
        }

        Theme.Tracked(g, label, font, ink, Math.Max(10, (Width - w) / 2), (Height - font.Height) / 2, tracking);
        font.Dispose();
    }
}

/// <summary>Checkbox drawn as a small console toggle.</summary>
sealed class FlatCheck : CheckBox
{
    private bool _hot;

    public FlatCheck(string text)
    {
        Text = text;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new Rectangle(0, (Height - 17) / 2, 17, 17);

        using (var path = Theme.Round(box, 5))
        {
            using (var b = new SolidBrush(Checked ? Theme.Accent : _hot ? Theme.SurfaceHover : Theme.SurfaceSunken)) g.FillPath(b, path);
            using var pen = new Pen(Checked ? Theme.Accent : _hot ? Theme.BorderHi : Theme.Border);
            g.DrawPath(pen, path);
        }

        if (Checked)
            using (var tick = new Pen(Color.FromArgb(0x16, 0x10, 0x06), 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLines(tick, new[]
                {
                    new PointF(box.X + 4.5f, box.Y + 8.8f),
                    new PointF(box.X + 7.3f, box.Y + 11.6f),
                    new PointF(box.X + 12.6f, box.Y + 5.4f)
                });
            }

        using var font = Theme.Display(8.5f, FontStyle.Regular);
        TextRenderer.DrawText(g, Text, font, new Rectangle(box.Right + 11, 0, Width - box.Right - 11, Height),
            _hot ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Small tracked uppercase section heading.</summary>
sealed class SectionLabel : Control
{
    public Color Ink { get; set; } = Theme.Faint;

    public SectionLabel(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintBackdrop(this, e.Graphics);
        using var font = Theme.Display(7.5f);
        var w = Theme.Tracked(e.Graphics, Text.ToUpperInvariant(), font, Ink, 0, Height - 15, 2);
        using var rule = new SolidBrush(Theme.Border);
        e.Graphics.FillRectangle(rule, new Rectangle(w + 12, Height - 8, Math.Max(0, Width - w - 12), 1));
    }
}

/// <summary>Wordmark plus endpoint readout.</summary>
sealed class HeaderBar : Control
{
    public string Endpoint { get; set; } = "";

    public HeaderBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintBackdrop(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var mark = new SolidBrush(Theme.Accent))
            g.FillRectangle(mark, new Rectangle(0, 6, 3, 26));

        using (var word = Theme.Display(15f))
            Theme.Tracked(g, "TOASTER", word, Theme.Text, 14, 4, 4);
        using (var tag = Theme.Mono(8f))
            TextRenderer.DrawText(g, "reusable technical memory · local mcp service", tag,
                new Point(15, 27), Theme.Faint, Theme.Flat);

        if (Endpoint.Length == 0) return;
        using var right = Theme.Mono(8.5f);
        var size = TextRenderer.MeasureText(g, Endpoint, right, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        TextRenderer.DrawText(g, Endpoint, right, new Point(Width - size.Width, 16), Theme.Muted, Theme.Flat);
    }
}
