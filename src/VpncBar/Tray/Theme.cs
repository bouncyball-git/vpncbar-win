namespace VpncBar.Tray;

// Cosmetic pass over a form's control tree. The experimental WinForms dark
// mode is harsh out of the box: pure-black surfaces with bright outlines.
// Design: in dark mode all inputs are BORDERLESS flat fills with no hover
// chrome — text boxes (borderless inside a FieldPanel), dropdowns
// (ThemedCombo) and buttons (ThemedButton) self-render — distinguished from
// the surface only by their fill color. Light mode keeps system rendering
// (the self-painted controls draw a classic light style themselves).
static class Theme
{
    // Soft dark palette (shared with ThemedButton / ThemedCombo / FieldPanel)
    internal static readonly Color Surface = Color.FromArgb(32, 32, 32);    // window + tab pages
    internal static readonly Color Field = Color.FromArgb(50, 50, 50);      // inputs at rest
    internal static readonly Color FieldHover = Color.FromArgb(60, 60, 60);
    internal static readonly Color FieldDown = Color.FromArgb(40, 40, 40);
    internal static readonly Color FieldDisabled = Color.FromArgb(42, 42, 42);  // dimmer than Field, lighter than Surface
    internal static readonly Color Text = Color.FromArgb(235, 235, 235);
    internal static readonly Color TextDisabled = Color.FromArgb(120, 120, 120);

    static bool Dark => Application.IsDarkModeEnabled;

    public static void Polish(Form form)
    {
        if (Dark) form.BackColor = Surface;
        Walk(form);
    }

    static void Walk(Control c)
    {
        switch (c)
        {
            case TextBox tb when Dark:
                tb.BackColor = Field;
                tb.ForeColor = Text;
                break;
            case ListView lv:
                lv.BorderStyle = BorderStyle.None;   // Fixed3D renders as a bright outline in dark mode
                if (Dark) { lv.BackColor = Surface; lv.ForeColor = Text; }
                break;
            case FieldPanel fp:
                fp.BackColor = Dark ? Field : SystemColors.Window;
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                if (Dark) page.BackColor = Surface;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel when Dark:
                c.BackColor = Surface;
                break;
        }
        foreach (Control child in c.Controls) Walk(child);
    }
}
