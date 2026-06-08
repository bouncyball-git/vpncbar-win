using VpncBar.Core;

namespace VpncBar.Tray;

// "Manage VPNs" window: profile list + Add / Edit / Remove / Import…
// Editors are opened through the tray context so each profile has at most one.
sealed class ManageForm : Form
{
    readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };
    readonly Action _onChange;
    readonly Action<Profile?> _openEditor;

    public ManageForm(Action onChange, Action<Profile?> openEditor)
    {
        _onChange = onChange;
        _openEditor = openEditor;

        Text = "Manage VPNs";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 300);
        Size = new Size(560, 360);
        MaximizeBox = false;
        ShowInTaskbar = true;

        _list.Columns.Add("Name", 180);
        _list.Columns.Add("Type", 90);
        _list.Columns.Add("Gateway", 230);
        _list.DoubleClick += (_, _) => EditSelected();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(Btn("Add", (_, _) => _openEditor(null)));
        buttons.Controls.Add(Btn("Edit", (_, _) => EditSelected()));
        buttons.Controls.Add(Btn("Remove", (_, _) => RemoveSelected()));
        buttons.Controls.Add(Btn("Import…", (_, _) => Import()));

        Controls.Add(_list);
        Controls.Add(buttons);
        Reload();
        Theme.Polish(this);
    }

    static ThemedButton Btn(string text, EventHandler onClick)
    {
        var b = new ThemedButton { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    public void Reload()
    {
        _list.Items.Clear();
        foreach (var p in ProfileStore.Load())
        {
            var item = new ListViewItem([p.Name, p.IsOpenconnect ? "openconnect" : "vpnc", p.Gateway])
            {
                Tag = p,
            };
            _list.Items.Add(item);
        }
    }

    Profile? Selected => _list.SelectedItems.Count > 0 ? (Profile)_list.SelectedItems[0].Tag! : null;

    void EditSelected()
    {
        if (Selected is Profile p) _openEditor(p);
    }

    void RemoveSelected()
    {
        if (Selected is not Profile p) return;
        var r = MessageBox.Show(this, $"Remove “{p.Name}”?\nIts stored secrets are deleted too.",
                                "VpncBar", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (r != DialogResult.OK) return;
        ProfileStore.Remove(p);
        Reload();
        _onChange();
    }

    // Import a Cisco .pcf or a vpnc .conf file. Obfuscated secrets are decoded
    // and stored straight into the Credential Manager; then report which
    // fields, if any, still need to be filled in (mac parity).
    void Import()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import VPN config",
            Filter = "VPN configs (*.pcf;*.conf)|*.pcf;*.conf|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var parsed = ConfigImport.Parse(dlg.FileName);
        if (parsed == null)
        {
            MessageBox.Show(this, "That file doesn't look like a Cisco .pcf or vpnc .conf.",
                            "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ProfileStore.Upsert(parsed.Profile, parsed.Secret, parsed.Password);
        Reload();
        _onChange();

        var missing = new List<string>();
        if (string.IsNullOrEmpty(parsed.Profile.Gateway)) missing.Add("Gateway");
        if (string.IsNullOrEmpty(parsed.Profile.Id)) missing.Add("Group name");
        if (parsed.Secret == null) missing.Add("Group secret");
        if (string.IsNullOrEmpty(parsed.Profile.Username)) missing.Add("Username");
        var msg = missing.Count == 0
            ? $"Imported “{parsed.Profile.Name}”."
            : $"Imported “{parsed.Profile.Name}”.\nStill needed: {string.Join(", ", missing)}.";
        MessageBox.Show(this, msg, "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
