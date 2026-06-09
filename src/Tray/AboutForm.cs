using System.Diagnostics;
using System.Reflection;

namespace VpncBar.Tray;

sealed class AboutForm : Form
{
    const string RepoUrl = "https://github.com/bouncyball-git/vpncbar-win";

    public AboutForm()
    {
        Text = "About VpncBar";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 200);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // full width, so Anchor=None centers

        Label Centered(string text, Font? font = null, Padding pad = default) => new()
        {
            Text = text,
            Font = font ?? Font,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = pad,
        };

        layout.Controls.Add(Centered("VpncBar", new Font(Font.FontFamily, 16, FontStyle.Bold)));
        layout.Controls.Add(Centered($"Version {version} — Windows port"));
        layout.Controls.Add(Centered(
            "A native Windows tray front-end for vpnc (Cisco IPSec)\nand openconnect (Cisco AnyConnect SSL).",
            pad: new Padding(0, 8, 0, 8)));

        var link = new LinkLabel
        {
            Text = "github.com/bouncyball-git/vpncbar-win",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
        };
        link.LinkClicked += (_, _) => OpenUrl(RepoUrl);
        layout.Controls.Add(link);

        Controls.Add(layout);
        Theme.Polish(this);
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { /* no default browser registered — nothing useful to do */ }
    }
}
