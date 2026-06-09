namespace VpncBar.Tray;

// Modal prompt for a one-time 2FA code before connecting an openconnect
// profile that needs one (profile's ocOtp flag). Returns the code, or null
// if cancelled. Mirrors the mac promptOTP().
static class OtpPrompt
{
    public static string? Show(string profileName)
    {
        using var form = new Form
        {
            Text = $"One-time code — {profileName}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(240, 88),
            TopMost = true,
        };
        var box = new TextBox();
        var field = new FieldPanel(box) { Dock = DockStyle.Top };
        field.Margin = new Padding(10, 0, 10, 0);
        var fieldHost = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 12, 10, 2) };
        field.Dock = DockStyle.Fill;
        fieldHost.Controls.Add(field);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };
        var ok = new ThemedButton { Text = "Connect", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new ThemedButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        form.Controls.Add(fieldHost);
        form.Controls.Add(buttons);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Polish(form);
        form.ActiveControl = box;

        // Always the top window with the caret ready (mac parity:
        // NSApp.activate(ignoringOtherApps:) + focusing the field on key).
        form.Shown += (_, _) =>
        {
            form.TopMost = true;
            form.Activate();
            form.BringToFront();
            box.Focus();
        };

        return form.ShowDialog() == DialogResult.OK && box.Text.Trim().Length > 0
            ? box.Text.Trim()
            : null;
    }
}
