/* Flint's look: one palette, a dark default and a light alternative.
 * Dark = near-black surround, grey panels, one mint accent. Everything that
 * paints itself reads these values at paint time, and Theme.Apply restyles the
 * standard WinForms controls, so switching theme needs no restart.
 * Settings (theme, collapsed sections) live in %APPDATA%\Flint\settings.json.
 */
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Flint;

public static class Theme
{
    public static bool Dark { get; private set; } = true;
    public static event Action? Changed;

    // surfaces, darkest to lightest
    public static Color CanvasBg, Panel, Panel2, Panel3, Border;
    // text
    public static Color Text, Muted;
    // accent (mint), pressed, and the text colour that sits on it
    public static Color Accent, AccentDark, OnAccent;
    public static Color Danger, Warn;

    private sealed class Data
    {
        public string Theme { get; set; } = "dark";
        public Dictionary<string, bool> Collapsed { get; set; } = new();
        public bool Frames { get; set; }
        public bool PixelSnap { get; set; } = true;
        public Dictionary<string, WorkspaceLayout> Workspaces { get; set; } = new();
        public WorkspaceLayout? LastWorkspace { get; set; }
        public List<string> RecentProjects { get; set; } = new();
    }
    private static Data _data = new();
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flint", "settings.json");

    static Theme()
    {
        try { if (File.Exists(SettingsPath)) _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch { _data = new(); }
        // FLINT_THEME overrides for one run (screenshots, tests) without touching the file.
        var env = Environment.GetEnvironmentVariable("FLINT_THEME");
        Palette(!(env ?? _data.Theme).Equals("light", StringComparison.OrdinalIgnoreCase));
    }

    private static Color H(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    private static void Palette(bool dark)
    {
        Dark = dark;
        if (dark)
        {
            CanvasBg = H(0x141517); Panel = H(0x1E1F22); Panel2 = H(0x26282C); Panel3 = H(0x2E3136); Border = H(0x3A3D43);
            Text = H(0xD7DADF); Muted = H(0x8B9098);
            Accent = H(0x3DDC97); AccentDark = H(0x27A872); OnAccent = H(0x0C1F17);
            Danger = H(0xE5605A); Warn = H(0xE0B050);
        }
        else
        {
            CanvasBg = H(0xD5D8DD); Panel = H(0xF1F2F4); Panel2 = H(0xFFFFFF); Panel3 = H(0xE3E5E9); Border = H(0xC5C9D0);
            Text = H(0x1F2328); Muted = H(0x626A73);
            Accent = H(0x17B26A); AccentDark = H(0x0E8A55); OnAccent = H(0x04281A);
            Danger = H(0xC9372F); Warn = H(0x9A6200);
        }
    }

    /// <summary>Switch theme, remember it, and tell every open window.</summary>
    public static void Set(bool dark)
    {
        if (dark == Dark) return;
        Palette(dark);
        _data.Theme = dark ? "dark" : "light";
        Save();
        Changed?.Invoke();
    }

    /// <summary>Bottom panel shows Frames view instead of Timeline (never restored in headless runs).</summary>
    public static bool FramesView { get => !Program.Headless && _data.Frames; set { _data.Frames = value; Save(); } }

    public static bool PixelSnap
    {
        get => Program.Headless || _data.PixelSnap;
        set { if (Program.Headless) return; _data.PixelSnap = value; Save(); }
    }

    /// <summary>The panel layout when Flint last closed (never read or written in headless runs).</summary>
    public static WorkspaceLayout? LastWorkspace
    {
        get => Program.Headless ? null : _data.LastWorkspace;
        set { if (Program.Headless) return; _data.LastWorkspace = value; Save(); }
    }

    // Saved workspace layouts. Headless runs (tests) keep them in memory only, so they never touch your settings file.
    private static readonly Dictionary<string, WorkspaceLayout> _testWorkspaces = new();
    private static Dictionary<string, WorkspaceLayout> Workspaces => Program.Headless ? _testWorkspaces : _data.Workspaces;
    public static IReadOnlyList<string> WorkspaceNames => Workspaces.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase).ToList();
    public static WorkspaceLayout? LoadWorkspace(string name) => Workspaces.TryGetValue(name, out var l) ? l : null;
    public static void SaveWorkspace(string name, WorkspaceLayout layout) { Workspaces[name] = layout; if (!Program.Headless) Save(); }
    public static void DeleteWorkspace(string name) { if (Workspaces.Remove(name) && !Program.Headless) Save(); }

    public static bool IsCollapsed(string key, bool dflt) => _data.Collapsed.TryGetValue(key, out var v) ? v : dflt;
    public static void SetCollapsed(string key, bool v) { _data.Collapsed[key] = v; Save(); }

    private static readonly List<string> _testRecentProjects = new();
    private static List<string> Recent => Program.Headless ? _testRecentProjects : (_data.RecentProjects ??= new());
    public static IReadOnlyList<string> RecentProjects => Recent;

    public static void RememberProject(string path)
    {
        path = Path.GetFullPath(path);
        var recent = Recent;
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        if (recent.Count > 8) recent.RemoveRange(8, recent.Count - 8);
        if (!Program.Headless) Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_data));
        }
        catch { /* settings are a convenience; never fail the app over them */ }
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Mint-tinted fill used for hover/selection, on top of <paramref name="under"/>.</summary>
    public static Color AccentSoft(Color under, float t = 0.18f) => Mix(under, Accent, t);

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- applying to controls ----------

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? ids);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    private static readonly ConditionalWeakTable<Control, object> _hooked = new();
    private static int Bgr(Color c) => c.R | (c.G << 8) | (c.B << 16);

    public static void DarkTitleBar(Form f)
    {
        int on = Dark ? 1 : 0;
        DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
        int cap = Bgr(Panel), txt = Bgr(Text);
        DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
        DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
    }

    public static void Apply(Control c)
    {
        Style(c);
        if (!_hooked.TryGetValue(c, out _))
        {
            _hooked.Add(c, c);
            c.ControlAdded += (_, e) => Apply(e.Control!);
            c.HandleCreated += (_, _) => NativeTheme(c);
        }
        if (c.IsHandleCreated) NativeTheme(c);
        foreach (Control k in c.Controls) Apply(k);
    }

    private static void NativeTheme(Control c)
    {
        if (!c.IsHandleCreated) return;
        string? name = c switch
        {
            ScrollBar => Dark ? "DarkMode_Explorer" : "Explorer",
            ComboBox => Dark ? "DarkMode_CFD" : "Explorer",
            NumericUpDown or ListBox or TextBox or ListView => Dark ? "DarkMode_Explorer" : "Explorer",
            ScrollableControl { AutoScroll: true } => Dark ? "DarkMode_Explorer" : "Explorer",
            _ => null,
        };
        if (name != null) SetWindowTheme(c.Handle, name, null);
        if (c is ProgressBar pb)
        {
            SetWindowTheme(pb.Handle, "", "");   // classic bar: honours the colour messages
            SendMessage(pb.Handle, 0x409, IntPtr.Zero, (IntPtr)Bgr(Accent));   // PBM_SETBARCOLOR
            SendMessage(pb.Handle, 0x2001, IntPtr.Zero, (IntPtr)Bgr(Panel3));  // PBM_SETBKCOLOR
        }
    }

    private static void Style(Control c)
    {
        switch (c)
        {
            case ToolStrip ts:
                ts.Renderer = new FlintRenderer();
                ts.BackColor = Panel;
                ts.ForeColor = Text;
                break;
            case IconButton or MintCheckBox or FlatGroup or FlatTabs:
                c.Invalidate();
                break;
            case Form f:
                f.BackColor = Panel; f.ForeColor = Text;
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.BorderSize = 1;
                b.UseVisualStyleBackColor = false;
                if (b.Text.Length > 0)
                {
                    b.BackColor = Panel3; b.ForeColor = Text;
                    b.FlatAppearance.MouseOverBackColor = AccentSoft(Panel3, 0.2f);
                    b.FlatAppearance.MouseDownBackColor = AccentSoft(Panel3, 0.4f);
                }
                if (b.DialogResult == DialogResult.OK)
                {
                    b.BackColor = Accent; b.ForeColor = OnAccent; b.FlatAppearance.BorderColor = AccentDark;
                    b.FlatAppearance.MouseOverBackColor = Mix(Accent, Text, 0.15f);
                    b.FlatAppearance.MouseDownBackColor = AccentDark;
                }
                break;
            case NumericUpDown or TextBox:
                c.BackColor = Panel2; c.ForeColor = Text;
                if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Standard;
                cb.BackColor = Panel2; cb.ForeColor = Text;
                break;
            case ListView lv:
                lv.BackColor = Panel2; lv.ForeColor = Text; lv.BorderStyle = BorderStyle.None;
                break;
            case ListBox lb:
                lb.BackColor = Panel2; lb.ForeColor = Text; lb.BorderStyle = BorderStyle.None;
                break;
            case CheckBox or RadioButton:
                c.ForeColor = Text;
                break;
            case Label l:
                if (l.Tag as string == "muted" || l.ForeColor == SystemColors.GrayText) { l.Tag = "muted"; l.ForeColor = Muted; }
                else if (l.Tag as string != "keep") l.ForeColor = Text;
                break;
            case CanvasPanel:
                c.BackColor = CanvasBg;
                break;
            case LayersPanel or TimelinePanel or LibraryPanel:
                c.BackColor = Panel2;
                break;
            case SplitContainer sc:
                sc.BackColor = Panel;
                break;
            case TabPage tp:
                tp.BackColor = Panel; tp.ForeColor = Text;
                break;
            case System.Windows.Forms.Panel when c.GetType() == typeof(System.Windows.Forms.Panel):
                if (c.Tag as string != "keep") c.BackColor = Panel;
                c.ForeColor = Text;
                break;
        }
    }

    /// <summary>A tooltip drawn in the theme instead of the pale system one.</summary>
    public static ToolTip MakeTip()
    {
        var t = new ToolTip { OwnerDraw = true, InitialDelay = 400, ReshowDelay = 100 };
        var font = new Font("Segoe UI", 8.5f);
        t.Popup += (_, e) =>
        {
            var s = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl!), font);
            e.ToolTipSize = new Size(Math.Min(s.Width + 14, 420), s.Height + 10);
        };
        t.Draw += (_, e) =>
        {
            using (var b = new SolidBrush(Panel3)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, new Rectangle(7, 4, e.Bounds.Width - 10, e.Bounds.Height - 6), Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        };
        return t;
    }
}

/// <summary>Base for every window: applies the theme and follows theme switches.</summary>
public class ThemedForm : Form
{
    public ThemedForm() => Theme.Changed += OnThemeChanged;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(this);
        Theme.DarkTitleBar(this);
    }

    protected virtual void OnThemeChanged()
    {
        Theme.Apply(this);
        if (IsHandleCreated) Theme.DarkTitleBar(this);
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

/// <summary>Menus, toolbar and status bar in the theme: flat, mint when hovered.</summary>
public sealed class FlintRenderer : ToolStripProfessionalRenderer
{
    private sealed class Colors : ProfessionalColorTable
    {
        public Colors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => Theme.Panel2;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.AccentSoft(Theme.Panel2, 0.2f);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => Theme.Panel3;
        public override Color MenuItemPressedGradientEnd => Theme.Panel3;
        public override Color MenuStripGradientBegin => Theme.Panel;
        public override Color MenuStripGradientEnd => Theme.Panel;
        public override Color ToolStripGradientBegin => Theme.Panel;
        public override Color ToolStripGradientMiddle => Theme.Panel;
        public override Color ToolStripGradientEnd => Theme.Panel;
        public override Color ToolStripBorder => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel2;
        public override Color ImageMarginGradientMiddle => Theme.Panel2;
        public override Color ImageMarginGradientEnd => Theme.Panel2;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color ButtonSelectedHighlight => Theme.AccentSoft(Theme.Panel);
        public override Color ButtonSelectedGradientBegin => Theme.AccentSoft(Theme.Panel);
        public override Color ButtonSelectedGradientMiddle => Theme.AccentSoft(Theme.Panel);
        public override Color ButtonSelectedGradientEnd => Theme.AccentSoft(Theme.Panel);
        public override Color ButtonSelectedBorder => Theme.Accent;
        public override Color ButtonPressedGradientBegin => Theme.AccentSoft(Theme.Panel, 0.35f);
        public override Color ButtonPressedGradientMiddle => Theme.AccentSoft(Theme.Panel, 0.35f);
        public override Color ButtonPressedGradientEnd => Theme.AccentSoft(Theme.Panel, 0.35f);
        public override Color ButtonPressedBorder => Theme.AccentDark;
        public override Color StatusStripGradientBegin => Theme.Panel;
        public override Color StatusStripGradientEnd => Theme.Panel;
        public override Color CheckBackground => Theme.AccentSoft(Theme.Panel2, 0.3f);
        public override Color CheckSelectedBackground => Theme.AccentSoft(Theme.Panel2, 0.4f);
        public override Color CheckPressedBackground => Theme.AccentSoft(Theme.Panel2, 0.4f);
    }

    public FlintRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);   // menus keep a hairline; bars get none
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(e.ToolStrip is ToolStripDropDown ? Theme.Panel2 : Theme.Panel);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }
}
