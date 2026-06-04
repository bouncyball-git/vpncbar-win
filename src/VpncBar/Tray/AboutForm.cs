using System.Diagnostics;
using System.Reflection;
using VpncBar.Core;

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
        ClientSize = new Size(380, 240);

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

        var autostart = new CheckBox
        {
            Text = "Start VpncBar at login",
            AutoSize = true,
            Checked = AutoStart.IsEnabled(),
            Padding = new Padding(0, 0, 0, 8),
        };
        autostart.CheckedChanged += (_, _) => AutoStart.SetEnabled(autostart.Checked);
        layout.Controls.Add(autostart);

        var uninstall = new ThemedButton { Text = "Uninstall VpncBar…", AutoSize = true };
        uninstall.Click += (_, _) => Uninstall();
        layout.Controls.Add(uninstall);

        Controls.Add(layout);
        Theme.Polish(this);
    }

    // Hand off to the installer's uninstaller (registered under the standard
    // uninstall key by Inno Setup). Profiles + stored secrets are kept.
    void Uninstall()
    {
        var uninstaller = FindUninstaller();
        if (uninstaller == null)
        {
            MessageBox.Show(this,
                "VpncBar doesn't appear to be installed via the installer.\n" +
                "If you're running it from a build folder, just delete that folder\n" +
                "(and run “VpncBar.exe --uninstall-service” from an elevated prompt\n" +
                "to remove the service).",
                "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var r = MessageBox.Show(this,
            "Uninstall VpncBar?\n\nYour profiles and saved passwords are kept.\nAll tunnels will be disconnected.",
            "VpncBar", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (r != DialogResult.OK) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uninstaller, UseShellExecute = true });
            Application.Exit();   // the uninstaller stops the service + removes us
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Couldn't launch the uninstaller:\n{e.Message}",
                            "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // The Inno Setup uninstaller path from the per-user/-machine uninstall key.
    static string? FindUninstaller()
    {
        string[] roots =
        [
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VpncBar_is1",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VpncBar_is1",
        ];
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            foreach (var path in roots)
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue("UninstallString") is string s)
                    return s.Trim('"');
            }
        }
        return null;
    }
}
