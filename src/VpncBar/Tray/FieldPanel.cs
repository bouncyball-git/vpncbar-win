namespace VpncBar.Tray;

// Chrome around a borderless TextBox: comfortable padding and height, fill
// color provided by Theme. In dark mode the field is a flat borderless fill
// (per design); in light mode it draws a classic 1px system border so the
// field doesn't vanish into a white window.
sealed class FieldPanel : Panel
{
    public FieldPanel(TextBox tb)
    {
        Padding = new Padding(7, 5, 7, 5);
        Height = 29;
        tb.BorderStyle = BorderStyle.None;
        tb.Dock = DockStyle.Fill;
        Controls.Add(tb);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Application.IsDarkModeEnabled)
        {
            using var pen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
