using System.Reflection;
using VpncBar.Core;
using VpncBar.Ipc;

namespace VpncBar.Tray;

// The tray application: a NotifyIcon whose context menu is the whole UI —
// one row per profile (✓ + name + right-aligned elapsed time), then
// Disconnect All / Manage VPNs… / About / Quit. Mirrors the mac menu bar.
sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 5000 };   // background state refresh
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };   // elapsed redraw while menu open
    readonly List<(ToolStripMenuItem Item, DateTime Since)> _liveRows = [];
    readonly Dictionary<string, ProfileEditorForm> _editors = [];            // one editor per profile (uuid)
    ManageForm? _manage;
    AboutForm? _about;
    HashSet<string>? _lastConnected;   // null until first poll (no notification at launch)

    public TrayContext()
    {
        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Disconnected,
            Text = "VpncBar",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // Left-click opens the same menu as right-click (mac: the menu IS the UI).
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(_icon, null);
        };

        _menu.Opening += (_, _) => { RefreshState(); _tick.Start(); };
        _menu.Closed += (_, _) => _tick.Stop();
        _tick.Tick += (_, _) => UpdateElapsed();

        _poll.Tick += (_, _) => RefreshState();
        _poll.Start();

        // Mac parity: tear down all tunnels when the tray session ends (logoff/
        // shutdown). Quit goes through ExitThread below; crash is the exemption.
        Application.ApplicationExit += (_, _) => TunnelClient.DisconnectAll();

        RefreshState();
    }

    void RefreshState()
    {
        var profiles = ProfileStore.Load();
        var tunnels = TunnelClient.Status(profiles);
        var connected = tunnels.Keys.ToHashSet();

        _icon.Icon = connected.Count == 0 ? TrayIcons.Disconnected : TrayIcons.Connected;
        // Never rebuild the menu while it's open (jitter): the 1s tick updates
        // the elapsed times in place, and Opening always rebuilds fresh (mac
        // parity — its poll timer is suspended during menu tracking).
        if (!_menu.Visible) RebuildMenu(profiles, tunnels);

        // Notify per profile on change (covers manual connects + unexpected drops).
        if (_lastConnected != null)
        {
            foreach (var name in connected.Except(_lastConnected))
                Notify("VPN connected", $"Connected to {name}.");
            foreach (var name in _lastConnected.Except(connected))
                Notify("VPN disconnected", $"Disconnected from {name}.");
        }
        _lastConnected = connected;
    }

    void RebuildMenu(List<Profile> profiles, Dictionary<string, DateTime> tunnels)
    {
        _menu.Items.Clear();
        _liveRows.Clear();

        if (profiles.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("No VPNs") { Enabled = false });
        }
        else
        {
            foreach (var p in profiles)
            {
                var connected = tunnels.TryGetValue(p.Name, out var since);
                var item = new ToolStripMenuItem(p.Name) { Checked = connected };
                if (connected)
                {
                    item.ShortcutKeyDisplayString = Format.Elapsed(DateTime.Now - since);
                    _liveRows.Add((item, since));
                }
                var profile = p;   // capture
                // Left-click toggles the tunnel; right-click opens the editor.
                item.MouseUp += (_, e) =>
                {
                    _menu.Close();
                    if (e.Button == MouseButtons.Right) OpenEditor(profile);
                    else if (e.Button == MouseButtons.Left) ToggleProfile(profile);
                };
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        if (tunnels.Count > 0)
            _menu.Items.Add("Disconnect All", null, (_, _) => { TunnelClient.DisconnectAll(); RefreshState(); });
        _menu.Items.Add("Manage VPNs…", null, (_, _) => OpenManage());
        _menu.Items.Add("About VpncBar", null, (_, _) => OpenAbout());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quit VpncBar", null, (_, _) => Quit());
    }

    void UpdateElapsed()
    {
        foreach (var (item, since) in _liveRows)
            item.ShortcutKeyDisplayString = Format.Elapsed(DateTime.Now - since);
    }

    void ToggleProfile(Profile p)
    {
        var connected = TunnelClient.Status([p]).Count > 0;
        string? otp = null;
        if (!connected && p.IsOpenconnect && (p.OcOtp ?? false))
        {
            otp = OtpPrompt.Show(p.Name);
            if (otp == null) return;   // cancelled
        }
        // Connect can take a while (gateway auth) — keep the UI responsive.
        Task.Run(() =>
        {
            var err = connected ? TunnelClient.Disconnect(p) : TunnelClient.Connect(p, otp);
            BeginInvoke(() =>
            {
                RefreshState();
                if (err != null) MessageBox.Show(err, "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        });
    }

    // ApplicationContext has no window; marshal back via the menu's handle.
    void BeginInvoke(Action action)
    {
        if (_menu.IsHandleCreated) _menu.BeginInvoke(action);
        else action();
    }

    // One editor window per profile (opening it again just brings it forward).
    public void OpenEditor(Profile? p)
    {
        var key = p?.Uuid ?? p?.Name ?? "__new__";
        if (_editors.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }
        var editor = new ProfileEditorForm(p, onSaved: () => { RefreshState(); _manage?.Reload(); });
        editor.FormClosed += (_, _) => _editors.Remove(key);
        _editors[key] = editor;
        editor.Show();
        editor.Activate();
    }

    void OpenManage()
    {
        if (_manage == null || _manage.IsDisposed)
        {
            _manage = new ManageForm(onChange: RefreshState, openEditor: OpenEditor);
        }
        _manage.Show();
        _manage.Activate();
    }

    void OpenAbout()
    {
        if (_about == null || _about.IsDisposed) _about = new AboutForm();
        _about.Show();
        _about.Activate();
    }

    void Notify(string title, string body)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = body;
        _icon.ShowBalloonTip(4000);
    }

    void Quit()
    {
        TunnelClient.DisconnectAll();   // mac parity: quit never orphans tunnels
        _icon.Visible = false;
        ExitThread();
    }
}
