using System.Runtime.InteropServices;

namespace VpncBar.Tray;

// Cosmetic pass over a form's control tree. The experimental WinForms dark
// mode is harsh out of the box: pure-black surfaces with bright outlines.
// Design: in dark mode all inputs are BORDERLESS flat fills — text boxes
// (borderless inside a FieldPanel), combos (FlatStyle.Popup), and buttons
// (ThemedButton self-renders) — distinguished from the surface only by their
// fill color. Light mode keeps system rendering.
static class Theme
{
    // Soft dark palette (shared with ThemedButton / FieldPanel)
    internal static readonly Color Surface = Color.FromArgb(32, 32, 32);    // window + tab pages
    internal static readonly Color Field = Color.FromArgb(50, 50, 50);      // inputs at rest
    internal static readonly Color FieldHover = Color.FromArgb(60, 60, 60);
    internal static readonly Color FieldDown = Color.FromArgb(40, 40, 40);
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
            case ComboBox cb when Dark:
                // FlatStyle.Popup honors BackColor (the default dark renderer
                // misses combos inside tab pages); DarkMode_CFD darkens the
                // dropdown list itself. Borderless at rest — by design.
                cb.FlatStyle = FlatStyle.Popup;
                cb.BackColor = Field;
                cb.ForeColor = Text;
                if (cb.IsHandleCreated) DarkenCombo(cb);
                else cb.HandleCreated += (s, _) => DarkenCombo((ComboBox)s!);
                break;
            case FieldPanel fp:
                if (Dark) fp.BackColor = Field;
                else fp.BackColor = SystemColors.Window;
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

    static void DarkenCombo(ComboBox cb)
    {
        SetWindowTheme(cb.Handle, "DarkMode_CFD", null);
    }

    [DllImport("uxtheme", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}
