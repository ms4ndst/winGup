using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinGup;

internal static class CatppuccinTheme
{
    // ── Mocha palette (all 26 colors) ────────────────────────────────────────
    public static readonly Color Rosewater = C("#f5e0dc");
    public static readonly Color Flamingo  = C("#f2cdcd");
    public static readonly Color Pink      = C("#f5c2e7");
    public static readonly Color Mauve     = C("#cba6f7");
    public static readonly Color Red       = C("#f38ba8");
    public static readonly Color Maroon    = C("#eba0ac");
    public static readonly Color Peach     = C("#fab387");
    public static readonly Color Yellow    = C("#f9e2af");
    public static readonly Color Green     = C("#a6e3a1");
    public static readonly Color Teal      = C("#94e2d5");
    public static readonly Color Sky       = C("#89dceb");
    public static readonly Color Sapphire  = C("#74c7ec");
    public static readonly Color Blue      = C("#89b4fa");
    public static readonly Color Lavender  = C("#b4befe");
    public static readonly Color Text      = C("#cdd6f4");
    public static readonly Color Subtext1  = C("#bac2de");
    public static readonly Color Subtext0  = C("#a6adc8");
    public static readonly Color Overlay2  = C("#9399b2");
    public static readonly Color Overlay1  = C("#7f849c");
    public static readonly Color Overlay0  = C("#6c7086");
    public static readonly Color Surface2  = C("#585b70");
    public static readonly Color Surface1  = C("#45475a");
    public static readonly Color Surface0  = C("#313244");
    public static readonly Color Base      = C("#1e1e2e");
    public static readonly Color Mantle    = C("#181825");
    public static readonly Color Crust     = C("#11111b");

    // ── Semantic aliases ─────────────────────────────────────────────────────
    public static Color Background       => Base;
    public static Color BackgroundRaised => Mantle;
    public static Color BackgroundDeep   => Crust;
    public static Color Primary          => Mauve;
    public static Color Secondary        => Blue;
    public static Color Accent           => Sapphire;
    public static Color Success          => Green;
    public static Color Warning          => Yellow;
    public static Color Error            => Red;
    public static Color Info             => Sky;
    public static Color TextPrimary      => Text;
    public static Color TextMuted        => Subtext1;
    public static Color TextFaint        => Overlay0;
    public static Color Border           => Overlay1;
    public static Color Hover            => Surface0;
    public static Color Active           => Surface1;

    // ── Form-level application ───────────────────────────────────────────────

    public static void ApplyToForm(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = TextPrimary;
        form.Font = new Font("Segoe UI", 9f);
        ApplyToControls(form.Controls);

        if (form.IsHandleCreated)
            ApplyDarkTitleBar(form);
        else
            form.HandleCreated += (_, _) => ApplyDarkTitleBar(form);
    }

    public static void ApplyToControls(Control.ControlCollection controls)
    {
        foreach (Control c in controls)
        {
            ApplyToControl(c);
            if (c.Controls.Count > 0)
                ApplyToControls(c.Controls);
        }
    }

    public static void ApplyToControl(Control control)
    {
        switch (control)
        {
            case Button btn:
                StyleButton(btn);
                break;
            case Label lbl:
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = TextMuted;
                break;
            case CheckBox chk:
                chk.BackColor = Color.Transparent;
                chk.ForeColor = TextPrimary;
                break;
            case DateTimePicker dtp:
                dtp.BackColor = Surface0;
                dtp.ForeColor = TextPrimary;
                break;
            case DataGridView dgv:
                StyleDataGridView(dgv);
                break;
            case RichTextBox:
                // Styled explicitly via StyleOutputBox
                break;
            case Panel pnl:
                pnl.BackColor = BackgroundRaised;
                break;
            case SplitContainer sc:
                sc.BackColor = Background;
                break;
        }
    }

    // ── Control-specific helpers ─────────────────────────────────────────────

    public static void StyleButton(Button btn, bool isPrimary = false)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = isPrimary ? Primary : Surface0;
        btn.ForeColor = isPrimary ? Crust    : TextPrimary;
        btn.FlatAppearance.BorderColor       = isPrimary ? Primary  : Overlay1;
        btn.FlatAppearance.BorderSize        = 1;
        btn.FlatAppearance.MouseOverBackColor = isPrimary ? Lavender : Surface1;
        btn.FlatAppearance.MouseDownBackColor = isPrimary ? Mauve    : Surface2;
        btn.Cursor = Cursors.Hand;
    }

    public static void StyleDataGridView(DataGridView dgv)
    {
        var cellFont   = new Font("Segoe UI", 9f);
        var headerFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        dgv.BackgroundColor = BackgroundRaised;
        dgv.GridColor       = Border;
        dgv.BorderStyle     = BorderStyle.None;

        dgv.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor        = BackgroundRaised,
            ForeColor        = TextPrimary,
            SelectionBackColor = Active,
            SelectionForeColor = TextPrimary,
            Font             = cellFont,
            Padding          = new Padding(4, 2, 4, 2),
        };

        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor        = BackgroundDeep,
            ForeColor        = TextMuted,
            SelectionBackColor = BackgroundDeep,
            SelectionForeColor = TextMuted,
            Font             = headerFont,
            Padding          = new Padding(4, 4, 4, 4),
        };

        dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor        = Background,
            ForeColor        = TextPrimary,
            SelectionBackColor = Active,
            SelectionForeColor = TextPrimary,
        };

        dgv.ColumnHeadersBorderStyle  = DataGridViewHeaderBorderStyle.Single;
        dgv.CellBorderStyle           = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.EnableHeadersVisualStyles = false;
        dgv.RowHeadersVisible         = false;
        dgv.RowTemplate.Height        = 26;
    }

    public static void StyleContextMenu(ContextMenuStrip menu)
    {
        menu.BackColor = BackgroundRaised;
        menu.ForeColor = TextPrimary;
        menu.Renderer  = new CatppuccinMenuRenderer();

        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = BackgroundRaised;
            item.ForeColor = TextPrimary;
        }
    }

    public static void StyleOutputBox(RichTextBox rtb)
    {
        rtb.BackColor   = BackgroundDeep;
        rtb.ForeColor   = TextPrimary;
        rtb.Font        = new Font("Consolas", 8.25f);
        rtb.BorderStyle = BorderStyle.None;
    }

    // ── DWM dark title bar (Windows 10 20H1+ / Windows 11) ──────────────────

    private static void ApplyDarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            int value = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
        }
        catch { /* best-effort — non-critical */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    // ── Context menu renderer ────────────────────────────────────────────────

    private sealed class CatppuccinMenuRenderer : ToolStripProfessionalRenderer
    {
        public CatppuccinMenuRenderer() : base(new CatppuccinColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var color = e.Item.Selected ? Active : BackgroundRaised;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(BackgroundRaised);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Surface2);
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? TextPrimary : TextFaint;
            base.OnRenderItemText(e);
        }
    }

    private sealed class CatppuccinColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder                      => Border;
        public override Color MenuItemBorder                  => Overlay1;
        public override Color MenuItemSelected                => Active;
        public override Color MenuItemSelectedGradientBegin   => Active;
        public override Color MenuItemSelectedGradientEnd     => Active;
        public override Color ToolStripDropDownBackground     => BackgroundRaised;
        public override Color ImageMarginGradientBegin        => BackgroundRaised;
        public override Color ImageMarginGradientMiddle       => BackgroundRaised;
        public override Color ImageMarginGradientEnd          => BackgroundRaised;
    }

    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
}
