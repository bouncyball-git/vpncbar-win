namespace VpncBar.Tray;

// Chrome around a borderless TextBox: comfortable padding and height, fill
// color provided by Theme, plus an optional accessory control docked inside
// the field's right edge (e.g. the password reveal eye). In dark mode the
// field is a flat borderless fill (per design); in light mode it draws a
// classic 1px system border so the field doesn't vanish into a white window.
sealed class FieldPanel : Panel
{
    public FieldPanel(TextBox tb, Control? accessory = null)
    {
        Padding = new Padding(7, 5, accessory == null ? 7 : 2, 5);
        Height = 29;
        tb.BorderStyle = BorderStyle.None;
        tb.Dock = DockStyle.Fill;
        Controls.Add(tb);                 // added first → fills what the accessory leaves
        if (accessory != null)
        {
            accessory.Dock = DockStyle.Right;
            Controls.Add(accessory);
        }
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

// Flat in-field "…" button (MDL2 More glyph): no chrome, shares the field's
// fill — used to open a file browser for path fields. Behavior comes from the
// standard Click event.
sealed class DotsButton : Control
{
    bool _hover;

    public DotsButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 30;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool dark = Application.IsDarkModeEnabled;
        var bg = Parent?.BackColor ?? (dark ? Theme.Field : SystemColors.Window);
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, ClientRectangle);

        var color = dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(95, 95, 95);
        if (_hover) color = dark ? Theme.Text : SystemColors.WindowText;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var glyphFont = new Font("Segoe MDL2 Assets", Font.Size + 1f);
        TextRenderer.DrawText(e.Graphics, ((char)0xE712).ToString(), glyphFont, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// Flat in-field password-reveal toggle: the Segoe MDL2 eye glyph, no chrome
// at all — it sits inside the FieldPanel and shares its fill. Click toggles
// the masked state of the TextBox it guards.
sealed class EyeButton : Control
{
    readonly TextBox _tb;

    public EyeButton(TextBox tb)
    {
        _tb = tb;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 30;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnClick(EventArgs e)
    {
        _tb.UseSystemPasswordChar = !_tb.UseSystemPasswordChar;
        Invalidate();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool dark = Application.IsDarkModeEnabled;
        var bg = Parent?.BackColor ?? (dark ? Theme.Field : SystemColors.Window);
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, ClientRectangle);

        bool revealed = !_tb.UseSystemPasswordChar;
        var color = dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(95, 95, 95);   // dimmed
        // Eye while masked (click to reveal); while revealed, the same eye with
        // a strike line through it, top-left to bottom-right (click to hide).
        // Drawn through a transform: the MDL2 eye is a wide, squat glyph, so
        // stretch it a bit vertically to fill the field better.
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var glyphFont = new Font("Segoe MDL2 Assets", Font.Size + 2f);
        using var brush = new SolidBrush(color);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TranslateTransform(Width / 2f, Height / 2f + 1.5f);   // optically centered, slightly low
        g.ScaleTransform(1f, 1.25f);
        g.DrawString(((char)0xE7B3).ToString(), glyphFont, brush, 0, 0, sf);
        if (revealed)
        {
            using var pen = new Pen(color, 1.6f);
            g.DrawLine(pen, -8f, -7f, 8f, 7f);
        }
        g.ResetTransform();
    }
}
