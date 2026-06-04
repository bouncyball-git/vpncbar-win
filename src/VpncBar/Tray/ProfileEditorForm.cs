using VpncBar.Core;

namespace VpncBar.Tray;

// Profile editor — one window per profile. A Type selector (vpnc | openconnect,
// locked once the profile is saved) switches the Credentials/Options content;
// Info and Debug become live in later phases. Mirrors the mac editor,
// including the authmode-dependent enabling of credential fields:
//
//   field          psk  hybrid  cert
//   Group secret    ✓     ·      ·
//   CA file         ·     ✓      ✓
//   Client cert     ·     ·      ✓
sealed class ProfileEditorForm : Form
{
    readonly Profile? _existing;
    readonly Action _onSaved;
    readonly ToolTip _tips = new();

    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    // Shared credentials (synced across the two type-specific panels on switch).
    readonly TextBox _name = new();
    readonly TextBox _gateway = new();
    readonly TextBox _username = new();
    readonly TextBox _password = new() { UseSystemPasswordChar = true };
    readonly TextBox _domains = new();
    readonly TextBox _clientCert = new();

    // vpnc credentials
    readonly TextBox _group = new();
    readonly TextBox _secret = new() { UseSystemPasswordChar = true };
    readonly ComboBox _authmode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _caFile = new();

    // openconnect credentials
    readonly ComboBox _ocGroup = new();   // editable; "Fetch groups" fills it in phase 3
    readonly TextBox _ocServerCert = new();
    readonly CheckBox _ocOtp = new() { Text = "Ask for one-time code (2FA) on connect", AutoSize = true };

    // vpnc options
    readonly ComboBox _dh = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _pfs = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _nat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _vendor = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _mtu = new() { PlaceholderText = "auto" };
    readonly TextBox _dpd = new() { PlaceholderText = "30" };
    readonly ComboBox _debug = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox _weak = new() { Text = "Enable weak encryption (3DES)", AutoSize = true, Checked = true };
    readonly CheckBox _singleDes = new() { Text = "Enable Single DES", AutoSize = true };
    readonly CheckBox _noEnc = new() { Text = "Enable no encryption", AutoSize = true };
    readonly CheckBox _weakAuth = new() { Text = "Enable weak authentication", AutoSize = true };

    // openconnect options
    readonly ComboBox _ocProto = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox _ocNoDtls = new() { Text = "Disable DTLS (force TLS transport)", AutoSize = true };
    readonly TextBox _ocDpd = new() { PlaceholderText = "gateway-negotiated" };
    readonly TextBox _ocMtu = new() { PlaceholderText = "auto" };
    readonly TextBox _ocReconnect = new() { PlaceholderText = "300" };
    readonly ComboBox _ocDebug = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    Panel _credsVpnc = null!, _credsOc = null!, _optsVpnc = null!, _optsOc = null!;

    bool IsOc => (string)_type.SelectedItem! == "openconnect";

    public ProfileEditorForm(Profile? existing, Action onSaved)
    {
        _existing = existing;
        _onSaved = onSaved;

        Text = existing == null ? "New VPN" : $"Edit VPN — {existing.Name}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 560);
        Size = MinimumSize;
        MaximizeBox = false;
        ShowInTaskbar = true;

        // --- Type selector (locked once saved, like the mac editor) ---
        _type.Items.AddRange(["vpnc", "openconnect"]);
        _type.SelectedItem = existing?.IsOpenconnect == true ? "openconnect" : "vpnc";
        _type.Enabled = existing == null;
        _type.SelectedIndexChanged += (_, _) => ApplyType();

        var typeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 0) };
        typeRow.Controls.Add(new Label { Text = "Type:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 4, 0) });
        typeRow.Controls.Add(_type);

        // --- Tabs ---
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCredentialsTab());
        tabs.TabPages.Add(BuildOptionsTab());
        tabs.TabPages.Add(PlaceholderTab("Info", "Live tunnel state appears here while connected.\n(Arrives with the service in phase 2.)"));
        tabs.TabPages.Add(PlaceholderTab("Debug", "The session log is tailed here while this tab is visible.\n(Arrives with the service in phase 2.)"));

        // --- Bottom buttons ---
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        var save = new ThemedButton { Text = "Save", AutoSize = true };
        save.Click += (_, _) => SaveProfile();
        var cancel = new ThemedButton { Text = "Cancel", AutoSize = true };
        cancel.Click += (_, _) => Close();
        var connect = new ThemedButton { Text = "Connect", AutoSize = true, Enabled = false };
        _tips.SetToolTip(connect, "Available once the service exists (phase 2)");
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);
        var spacer = new Panel { Width = 120, Height = 1 };
        bottom.Controls.Add(spacer);
        bottom.Controls.Add(connect);
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(typeRow);
        Controls.Add(bottom);

        PopulateOptionLists();
        LoadProfile();
        ApplyType();
        ApplyAuthmode();
        Theme.Polish(this);
        ActiveControl = _name;   // don't open with the Type combo focus-highlighted
    }

    // ----- layout helpers -----

    static TableLayoutPanel NewGrid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    static void AddRow(TableLayoutPanel t, string label, Control c, Control? extra = null)
    {
        if (c is TextBox tb) c = new FieldPanel(tb);   // borderless field chrome
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 0) }, 0, row);
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        t.Controls.Add(c, 1, row);
        if (extra != null) t.Controls.Add(extra, 2, row);
        else t.SetColumnSpan(c, 2);
    }

    static void AddFull(TableLayoutPanel t, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, 1, row);
        t.SetColumnSpan(c, 2);
    }

    ThemedButton RevealButton(TextBox tb)
    {
        var b = new ThemedButton { Text = "Show", AutoSize = true };
        b.Click += (_, _) =>
        {
            tb.UseSystemPasswordChar = !tb.UseSystemPasswordChar;
            b.Text = tb.UseSystemPasswordChar ? "Show" : "Hide";
        };
        return b;
    }

    ThemedButton BrowseButton(TextBox tb, string title)
    {
        var b = new ThemedButton { Text = "…", AutoSize = true };
        b.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = title };
            if (dlg.ShowDialog(this) == DialogResult.OK) tb.Text = dlg.FileName;
        };
        return b;
    }

    static TabPage PlaceholderTab(string title, string text)
    {
        var page = new TabPage(title);
        page.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
        });
        return page;
    }

    // ----- tabs -----

    TabPage BuildCredentialsTab()
    {
        var page = new TabPage("Credentials") { AutoScroll = true };

        var v = NewGrid();
        AddRow(v, "Name", _name);
        AddRow(v, "Gateway", _gateway);
        AddRow(v, "Group name", _group);
        AddRow(v, "Group secret", _secret, RevealButton(_secret));
        AddRow(v, "Username", _username);
        AddRow(v, "Password", _password, RevealButton(_password));
        AddRow(v, "VPN domains", _domains);
        _domains.PlaceholderText = "example.com, corp.local";
        AddRow(v, "IKE Authmode", _authmode);
        AddRow(v, "CA file", _caFile, BrowseButton(_caFile, "Choose CA certificate"));
        AddRow(v, "Client cert", _clientCert, BrowseButton(_clientCert, "Choose client certificate"));
        _credsVpnc = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _credsVpnc.Controls.Add(v);

        var o = NewGrid();
        var ocName = new TextBox();          // mirrors of the shared fields (a control
        var ocGateway = new TextBox();       // can only live in one parent panel)
        var ocUsername = new TextBox();
        var ocPassword = new TextBox { UseSystemPasswordChar = true };
        var ocDomains = new TextBox { PlaceholderText = "example.com, corp.local" };
        var ocClientCert = new TextBox();
        BindMirror(_name, ocName); BindMirror(_gateway, ocGateway); BindMirror(_username, ocUsername);
        BindMirror(_password, ocPassword); BindMirror(_domains, ocDomains); BindMirror(_clientCert, ocClientCert);
        AddRow(o, "Name", ocName);
        AddRow(o, "Gateway", ocGateway);
        var fetch = new ThemedButton { Text = "Fetch groups", AutoSize = true, Enabled = false };
        _tips.SetToolTip(fetch, "Contacts the gateway for its group list (phase 3)");
        AddRow(o, "Auth group", _ocGroup, fetch);
        AddRow(o, "Server cert", _ocServerCert);
        _ocServerCert.PlaceholderText = "pin-sha256:…";
        AddRow(o, "Username", ocUsername);
        AddRow(o, "Password", ocPassword, RevealButton(ocPassword));
        AddRow(o, "VPN domains", ocDomains);
        AddRow(o, "Client cert", ocClientCert, BrowseButton(ocClientCert, "Choose client certificate"));
        AddFull(o, _ocOtp);
        _credsOc = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
        _credsOc.Controls.Add(o);

        page.Controls.Add(_credsOc);
        page.Controls.Add(_credsVpnc);
        return page;
    }

    // Keep two TextBoxes (one per type panel) holding the same logical value.
    static void BindMirror(TextBox a, TextBox b)
    {
        bool guard = false;
        a.TextChanged += (_, _) => { if (!guard) { guard = true; b.Text = a.Text; guard = false; } };
        b.TextChanged += (_, _) => { if (!guard) { guard = true; a.Text = b.Text; guard = false; } };
    }

    TabPage BuildOptionsTab()
    {
        var page = new TabPage("Options") { AutoScroll = true };

        var v = NewGrid();
        AddRow(v, "IKE DH Group", _dh);
        AddRow(v, "Perfect Forward Secrecy", _pfs);
        AddRow(v, "NAT-T Mode", _nat);
        AddRow(v, "Vendor", _vendor);
        AddRow(v, "Interface MTU", _mtu);
        AddRow(v, "DPD timeout (s)", _dpd);
        AddRow(v, "Debug level", _debug);
        AddFull(v, _weak);
        AddFull(v, _singleDes);
        AddFull(v, _noEnc);
        AddFull(v, _weakAuth);
        _optsVpnc = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _optsVpnc.Controls.Add(v);

        var o = NewGrid();
        AddRow(o, "Protocol", _ocProto);
        AddFull(o, _ocNoDtls);
        AddRow(o, "DPD interval (s)", _ocDpd);
        AddRow(o, "MTU", _ocMtu);
        AddRow(o, "Reconnect timeout (s)", _ocReconnect);
        AddRow(o, "Debug level", _ocDebug);
        _optsOc = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
        _optsOc.Controls.Add(o);

        page.Controls.Add(_optsOc);
        page.Controls.Add(_optsVpnc);
        return page;
    }

    void PopulateOptionLists()
    {
        _authmode.Items.AddRange(["psk", "hybrid", "cert"]);
        _authmode.SelectedItem = "psk";
        _authmode.SelectedIndexChanged += (_, _) => ApplyAuthmode();

        _dh.Items.AddRange(["dh1", "dh2", "dh5", "dh14", "dh15", "dh16", "dh17", "dh18"]);
        _dh.SelectedItem = "dh2";
        _pfs.Items.AddRange(["server", "nopfs", "dh1", "dh2", "dh5"]);
        _pfs.SelectedItem = "server";
        _nat.Items.AddRange(["natt", "none", "force-natt", "cisco-udp"]);
        _nat.SelectedItem = "natt";
        _vendor.Items.AddRange(["cisco", "netscreen", "fortigate"]);
        _vendor.SelectedItem = "cisco";
        _debug.Items.AddRange(["0", "1", "2", "3", "99"]);
        _debug.SelectedItem = "0";

        _ocProto.Items.AddRange(["anyconnect", "gp", "pulse", "f5", "fortinet", "nc", "array"]);
        _ocProto.SelectedItem = "anyconnect";
        _ocDebug.Items.AddRange(["0", "1", "2", "3", "99"]);
        _ocDebug.SelectedItem = "1";
    }

    void ApplyType()
    {
        bool oc = IsOc;
        _credsVpnc.Visible = !oc;
        _credsOc.Visible = oc;
        _optsVpnc.Visible = !oc;
        _optsOc.Visible = oc;
    }

    // Gray out the credential fields that don't apply to the selected authmode.
    void ApplyAuthmode()
    {
        var mode = (string)(_authmode.SelectedItem ?? "psk");
        _secret.Enabled = mode == "psk";
        _caFile.Enabled = mode is "hybrid" or "cert";
        _clientCert.Enabled = mode == "cert" || IsOc;   // openconnect always allows a client cert
    }

    // ----- load / save -----

    void LoadProfile()
    {
        var p = _existing;
        if (p == null) return;

        _name.Text = p.Name;
        _gateway.Text = p.Gateway;
        _username.Text = p.Username;
        _domains.Text = p.DnsMatchDomains ?? "";
        _clientCert.Text = p.ClientCert ?? "";
        _password.Text = ProfileStore.Password(p) ?? "";

        _group.Text = p.Id;
        _secret.Text = ProfileStore.Secret(p) ?? "";
        if (Profile.Ne(p.Authmode) is string am && _authmode.Items.Contains(am)) _authmode.SelectedItem = am;
        _caFile.Text = p.CaFile ?? "";

        void Sel(ComboBox cb, string? v) { if (Profile.Ne(v) is string s && cb.Items.Contains(s)) cb.SelectedItem = s; }
        Sel(_dh, p.DhGroup);
        Sel(_pfs, p.Pfs);
        Sel(_nat, p.NatMode);
        Sel(_vendor, p.Vendor);
        _mtu.Text = p.Mtu ?? "";
        _dpd.Text = p.DpdTimeout ?? "";
        Sel(_debug, p.Debug);
        _weak.Checked = p.EnableWeak ?? true;
        _singleDes.Checked = p.SingleDES ?? false;
        _noEnc.Checked = p.NoEncryption ?? false;
        _weakAuth.Checked = p.WeakAuth ?? false;

        _ocGroup.Text = p.OcAuthgroup ?? "";
        _ocServerCert.Text = p.OcServerCert ?? "";
        _ocOtp.Checked = p.OcOtp ?? false;
        Sel(_ocProto, p.OcProtocol);
        _ocNoDtls.Checked = p.OcNoDTLS ?? false;
        _ocDpd.Text = p.OcDPD ?? "";
        _ocMtu.Text = p.OcMTU ?? "";
        _ocReconnect.Text = p.OcReconnect ?? "";
        Sel(_ocDebug, p.OcDebug);
    }

    void SaveProfile()
    {
        var name = _name.Text.Trim();
        var gateway = _gateway.Text.Trim();
        if (name.Length == 0 || gateway.Length == 0)
        {
            MessageBox.Show(this, "Name and Gateway are required.", "VpncBar",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // A combo selection equal to the backend's default is stored as null —
        // "directive omitted" — keeping profiles.json minimal and mac-compatible.
        static string? Def(ComboBox cb, string def)
        {
            var v = (string?)cb.SelectedItem;
            return v == def ? null : v;
        }

        var p = _existing ?? new Profile();   // existing keeps uuid + unedited fields
        p.Kind = IsOc ? "openconnect" : null;
        p.Name = name;
        p.Gateway = gateway;
        p.Username = _username.Text.Trim();
        p.DnsMatchDomains = Profile.Ne(_domains.Text);
        p.ClientCert = Profile.Ne(_clientCert.Text);

        if (IsOc)
        {
            p.OcAuthgroup = Profile.Ne(_ocGroup.Text);
            p.OcServerCert = Profile.Ne(_ocServerCert.Text);
            p.OcOtp = _ocOtp.Checked ? true : null;
            p.OcProtocol = Def(_ocProto, "anyconnect");
            p.OcNoDTLS = _ocNoDtls.Checked ? true : null;
            p.OcDPD = Profile.Ne(_ocDpd.Text);
            p.OcMTU = Profile.Ne(_ocMtu.Text);
            p.OcReconnect = Profile.Ne(_ocReconnect.Text);
            p.OcDebug = Def(_ocDebug, "1");
        }
        else
        {
            p.Id = _group.Text.Trim();
            p.Authmode = Def(_authmode, "psk");
            p.CaFile = Profile.Ne(_caFile.Text);
            p.DhGroup = Def(_dh, "dh2");
            p.Pfs = Def(_pfs, "server");
            p.NatMode = Def(_nat, "natt");
            p.Vendor = Def(_vendor, "cisco");
            p.Mtu = Profile.Ne(_mtu.Text);
            p.DpdTimeout = Profile.Ne(_dpd.Text);
            p.Debug = Def(_debug, "0");
            p.EnableWeak = _weak.Checked ? null : false;   // default is ON
            p.SingleDES = _singleDes.Checked ? true : null;
            p.NoEncryption = _noEnc.Checked ? true : null;
            p.WeakAuth = _weakAuth.Checked ? true : null;
        }

        ProfileStore.Upsert(p, Profile.Ne(_secret.Text), Profile.Ne(_password.Text));
        _onSaved();
        Close();
    }
}
