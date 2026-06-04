using System.Reflection;

namespace VpncBar.Tray;

sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About VpncBar";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 190);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
        layout.Controls.Add(new Label
        {
            Text = "VpncBar",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
        });
        layout.Controls.Add(new Label { Text = $"Version {version} — Windows port", AutoSize = true });
        layout.Controls.Add(new Label
        {
            Text = "Cisco IPSec (vpnc) and AnyConnect (openconnect)\ntray client.",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8),
        });

        var uninstall = new ThemedButton { Text = "Uninstall VpncBar…", AutoSize = true, Enabled = false };
        new ToolTip().SetToolTip(uninstall, "Available once the installer exists (phase 5)");
        layout.Controls.Add(uninstall);

        Controls.Add(layout);
        Theme.Polish(this);
    }
}
