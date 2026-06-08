using System.ComponentModel;

namespace VpncBar.Tray;

// Fully self-painted dropdown (DropDownList semantics). Replaces ComboBox
// because every native FlatStyle hot-tracks a border on mouse-over — there is
// no setting that disables it. This control paints a static flat field (no
// hover chrome whatsoever); the open list is a borderless popup form with an
// owner-drawn ListBox, so the row highlight spans the full dropdown width
// (a ToolStrip menu only highlights each item's natural width).
sealed class ThemedCombo : Control
{
    const int RowHeight = 24;
    const int MaxVisibleRows = 12;

    readonly List<string> _items = [];
    int _selected = -1;
    long _popupClosedAt;   // guards against click-to-close immediately reopening

    public ThemedCombo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(140, 29);   // match FieldPanel height so rows line up
    }

    public List<string> Items => _items;

    // Widen the closed control to fit its widest item (text pad + chevron),
    // so the popup list and the field are exactly the same width.
    public void SizeToItems()
    {
        int w = 0;
        foreach (var s in _items) w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
        Width = w + 8 + 30;
    }

    public event EventHandler? SelectedIndexChanged;

    [DefaultValue(null)]
    public string? SelectedItem
    {
        get => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;
        set
        {
            int i = value == null ? -1 : _items.IndexOf(value);
            if (i == _selected) return;
            _selected = i;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]   // match Control.Text's nullable setter
    public override string Text
    {
        get => SelectedItem ?? "";
        set => SelectedItem = value;
    }

    void Select(int index)
    {
        if (index < 0 || index >= _items.Count || index == _selected) return;
        _selected = index;
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    void OpenList()
    {
        if (_items.Count == 0) return;
        // The mousedown that dismissed a still-open popup arrives right after
        // its Deactivate→Close — don't treat it as "open again".
        if (Environment.TickCount64 - _popupClosedAt < 250) return;

        bool dark = Application.IsDarkModeEnabled;
        var back = dark ? Theme.Surface : SystemColors.Window;
        var fore = dark ? Theme.Text : SystemColors.WindowText;
        var highlight = dark ? Theme.FieldHover : SystemColors.Highlight;
        var highlightText = dark ? Theme.Text : SystemColors.HighlightText;

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = RowHeight,
            IntegralHeight = false,
            BackColor = back,
            ForeColor = fore,
        };
        foreach (var s in _items) list.Items.Add(s);
        list.SelectedIndex = _selected;

        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? highlight : back))
                e.Graphics.FillRectangle(b, e.Bounds);   // full row width
            TextRenderer.DrawText(e.Graphics, _items[e.Index], Font,
                Rectangle.Inflate(e.Bounds, -6, 0), sel ? highlightText : fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };

        int rows = Math.Min(_items.Count, MaxVisibleRows);
        var popup = new PopupForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            MinimumSize = Size.Empty,
            Size = new Size(Width, rows * RowHeight + 2),
            Location = PointToScreen(new Point(0, Height)),
            Padding = new Padding(1),                                  // 1px frame…
            BackColor = dark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark,   // …in this color
        };
        popup.Controls.Add(list);

        void Commit()
        {
            if (list.SelectedIndex >= 0) Select(list.SelectedIndex);
            popup.Close();
        }
        // Hover moves the highlight (classic combo dropdown behavior).
        list.MouseMove += (_, e) =>
        {
            int i = list.IndexFromPoint(e.Location);
            if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i;
        };
        list.Click += (_, _) => Commit();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { popup.Close(); e.Handled = true; }
        };
        popup.Deactivate += (_, _) => popup.Close();
        popup.FormClosed += (_, _) => _popupClosedAt = Environment.TickCount64;

        popup.Show(FindForm());
        list.Focus();
        if (_selected >= 0) list.TopIndex = Math.Max(0, _selected - rows + 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && Enabled) OpenList();
        base.OnMouseDown(e);
    }

    // Borderless popup that can be narrower than Windows' default minimum
    // window tracking width (~170px at 125% DPI) — otherwise the list ends up
    // wider than the combo it belongs to.
    sealed class PopupForm : Form
    {
        protected override void WndProc(ref Message m)
        {
            const int WM_GETMINMAXINFO = 0x24;
            base.WndProc(ref m);
            if (m.Msg == WM_GETMINMAXINFO)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
                mmi.ptMinTrackSize = new Point(1, 1);
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, m.LParam, false);
            }
        }

#pragma warning disable CS0649 // layout struct — the OS fills these; the unused
                               // fields keep ptMinTrackSize at the right offset.
        struct MINMAXINFO
        {
            public Point ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }
#pragma warning restore CS0649
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when e.Alt:
            case Keys.F4:
            case Keys.Space:
            case Keys.Enter:
                OpenList();
                e.Handled = true;
                break;
            case Keys.Down:
                Select(Math.Min(_selected + 1, _items.Count - 1));
                e.Handled = true;
                break;
            case Keys.Up:
                Select(Math.Max(_selected - 1, 0));
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool dark = Application.IsDarkModeEnabled;
        var fill = dark ? Theme.Field : SystemColors.Window;
        var textColor = !Enabled ? Theme.TextDisabled : dark ? Theme.Text : SystemColors.WindowText;

        using (var b = new SolidBrush(fill)) g.FillRectangle(b, ClientRectangle);
        if (!dark)   // light mode keeps a classic 1px border (like FieldPanel)
        {
            using var pen = new Pen(SystemColors.ControlDark);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        var textRect = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Chevron, drawn with the Segoe MDL2 glyph used across Windows.
        using var glyphFont = new Font("Segoe MDL2 Assets", Font.Size - 1f);
        var glyphRect = new Rectangle(Width - 24, 0, 20, Height);
        TextRenderer.DrawText(g, ((char)0xE70D).ToString(), glyphFont, glyphRect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        // No focus rectangle by design — flat and quiet, like the other fields.
    }
}
