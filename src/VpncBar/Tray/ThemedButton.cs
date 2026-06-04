namespace VpncBar.Tray;

// Fully self-painted button. Deliberately NOT derived from Button: the
// .NET 10 dark-mode renderer paints Button's bright rounded frame at a level
// that overrides OnPaint/OnPaintBackground/FlatAppearance/SetWindowTheme, so
// the only reliable way to get the app's borderless flat design is to own
// the whole control. Implements IButtonControl so AcceptButton/CancelButton
// (Enter/Esc) behave like a real button; in light mode it renders with the
// native visual-style pushbutton.
sealed class ThemedButton : Control, IButtonControl
{
    bool _hover, _down;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Size = new Size(88, 28);
    }

    [System.ComponentModel.DefaultValue(DialogResult.None)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) => Invalidate();

    public void PerformClick()
    {
        if (Enabled && Visible) OnClick(EventArgs.Empty);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + 26, text.Height + 12);
    }

    protected override void OnClick(EventArgs e)
    {
        if (DialogResult != DialogResult.None && FindForm() is Form f) f.DialogResult = DialogResult;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    // Space activates the focused button (Enter is routed by the form via
    // IButtonControl when this is the AcceptButton).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { _down = true; Invalidate(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _down)
        {
            _down = false;
            Invalidate();
            PerformClick();
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Application.IsDarkModeEnabled)
        {
            var fill = !Enabled ? Theme.Surface
                     : _down ? Theme.FieldDown
                     : _hover || Focused ? Theme.FieldHover
                     : Theme.Field;
            using var b = new SolidBrush(fill);
            g.FillRectangle(b, ClientRectangle);
            var textColor = Enabled ? Theme.Text : Theme.TextDisabled;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            // Native pushbutton look in light mode.
            var state = !Enabled ? System.Windows.Forms.VisualStyles.PushButtonState.Disabled
                      : _down ? System.Windows.Forms.VisualStyles.PushButtonState.Pressed
                      : _hover ? System.Windows.Forms.VisualStyles.PushButtonState.Hot
                      : Focused ? System.Windows.Forms.VisualStyles.PushButtonState.Default
                      : System.Windows.Forms.VisualStyles.PushButtonState.Normal;
            ButtonRenderer.DrawButton(g, ClientRectangle, Text, Font, Focused, state);
        }
    }
}
