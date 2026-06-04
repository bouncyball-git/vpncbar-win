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

    readonly ThemedCombo _type = new();

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
    readonly ThemedCombo _authmode = new();
    readonly TextBox _caFile = new();

    // openconnect credentials
    readonly TextBox _ocGroup = new();   // free text; "Fetch groups" (phase 3) adds a picker
    readonly TextBox _ocServerCert = new();
    readonly CheckBox _ocOtp = new() { Text = "Ask for one-time code (2FA) on connect", AutoSize = true };

    // vpnc options
    readonly ThemedCombo _dh = new();
    readonly ThemedCombo _pfs = new();
    readonly ThemedCombo _nat = new();
    readonly ThemedCombo _vendor = new();
    readonly TextBox _mtu = new() { PlaceholderText = "auto" };
    readonly TextBox _dpd = new() { PlaceholderText = "30" };
    readonly ThemedCombo _debug = new();
    readonly CheckBox _weak = new() { Text = "Enable weak encryption (3DES)", AutoSize = true, Checked = true };
    readonly CheckBox _singleDes = new() { Text = "Enable Single DES", AutoSize = true };
    readonly CheckBox _noEnc = new() { Text = "Enable no encryption", AutoSize = true };
    readonly CheckBox _weakAuth = new() { Text = "Enable weak authentication", AutoSize = true };

    // openconnect options
    readonly ThemedCombo _ocProto = new();
    readonly CheckBox _ocNoDtls = new() { Text = "Disable DTLS (force TLS transport)", AutoSize = true };
    readonly TextBox _ocDpd = new() { PlaceholderText = "gateway-negotiated" };
    readonly TextBox _ocMtu = new() { PlaceholderText = "auto" };
    readonly TextBox _ocReconnect = new() { PlaceholderText = "300" };
    readonly ThemedCombo _ocDebug = new();

    Panel _credsVpnc = null!, _credsOc = null!, _optsVpnc = null!, _optsOc = null!;
    TabControl _tabs = null!;
    ThemedButton _connect = null!;
    TextBox _debugLog = null!;
    readonly System.Windows.Forms.Timer _stateTimer = new() { Interval = 2000 };   // Connect-button label
    readonly System.Windows.Forms.Timer _tailTimer = new() { Interval = 250 };     // Debug-tab tail (~4×/s)
    long _logLength = -1;   // last tailed file size (skip re-reads when unchanged)

    bool IsOc => (string)_type.SelectedItem! == "openconnect";

    public ProfileEditorForm(Profile? existing, Action onSaved)
    {
        _existing = existing;
        _onSaved = onSaved;

        Text = existing == null ? "New VPN" : $"Edit VPN — {existing.Name}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(440, 570);
        Size = MinimumSize;
        MaximizeBox = false;
        ShowInTaskbar = true;

        // --- Type selector (locked once saved, like the mac editor) ---
        _type.Items.AddRange(["vpnc", "openconnect"]);
        _type.SizeToItems();   // field exactly as wide as its widest item (= popup width)
        _type.SelectedItem = existing?.IsOpenconnect == true ? "openconnect" : "vpnc";
        _type.Enabled = existing == null;
        _type.SelectedIndexChanged += (_, _) => ApplyType();

        var typeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 0) };
        typeRow.Controls.Add(new Label { Text = "Type:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 4, 0) });
        typeRow.Controls.Add(_type);

        // --- Tabs ---
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildCredentialsTab());
        _tabs.TabPages.Add(BuildOptionsTab());
        _tabs.TabPages.Add(PlaceholderTab("Info", "Live tunnel state appears here while connected.\n(Arrives with the network config in phase 3.)"));
        _tabs.TabPages.Add(BuildDebugTab());
        _tabs.SelectedIndexChanged += (_, _) => UpdateLogTailing();

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
        _connect = new ThemedButton { Text = "Connect", AutoSize = true, Enabled = existing != null };
        if (existing == null) _tips.SetToolTip(_connect, "Save the profile first");
        _connect.Click += (_, _) => ToggleConnect();
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);
        var spacer = new Panel { Width = 120, Height = 1 };
        bottom.Controls.Add(spacer);
        bottom.Controls.Add(_connect);
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(_tabs);
        Controls.Add(typeRow);
        Controls.Add(bottom);

        PopulateOptionLists();
        LoadProfile();
        ApplyType();
        ApplyAuthmode();
        Theme.Polish(this);
        ActiveControl = _name;   // don't open with the Type combo focus-highlighted

        // Track the live tunnel state: Connect/Disconnect button label (mac
        // parity) — polled off the UI thread so a slow pipe never stutters it.
        if (existing != null)
        {
            _stateTimer.Tick += (_, _) => PollState();
            _stateTimer.Start();
            PollState();
        }
        _tailTimer.Tick += (_, _) => TailLog();
        FormClosed += (_, _) => { _stateTimer.Stop(); _tailTimer.Stop(); };
    }

    // ----- live state (Connect button) -----

    bool _polling;
    bool _connected;

    void PollState()
    {
        if (_polling || _existing == null) return;
        _polling = true;
        Task.Run(() =>
        {
            var up = Ipc.TunnelClient.Status([_existing]).ContainsKey(_existing.Name);
            try
            {
                BeginInvoke(() =>
                {
                    _polling = false;
                    _connected = up;
                    _connect.Text = up ? "Disconnect" : "Connect";
                });
            }
            catch (InvalidOperationException) { _polling = false; /* form closed */ }
        });
    }

    void ToggleConnect()
    {
        if (_existing == null) return;
        var p = _existing;
        bool up = _connected;
        _connect.Enabled = false;
        Task.Run(() =>
        {
            var err = up ? Ipc.TunnelClient.Disconnect(p) : Ipc.TunnelClient.Connect(p);
            try
            {
                BeginInvoke(() =>
                {
                    _connect.Enabled = true;
                    PollState();
                    _onSaved();   // poke the tray to refresh its menu/icon
                    if (err != null)
                        MessageBox.Show(this, err, "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }
            catch (InvalidOperationException) { /* form closed */ }
        });
    }

    // ----- Debug tab (live log tail) -----

    TabPage BuildDebugTab()
    {
        var page = new TabPage("Debug");
        _debugLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4),
        };
        var clear = new ThemedButton { Text = "Clear log", AutoSize = true };
        clear.Click += (_, _) => ClearLog();
        var reveal = new ThemedButton { Text = "Reveal log", AutoSize = true };
        reveal.Click += (_, _) =>
        {
            if (_existing != null && File.Exists(Paths.LogFile(_existing)))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Paths.LogFile(_existing)}\"");
        };
        buttons.Controls.Add(clear);
        buttons.Controls.Add(reveal);
        page.Controls.Add(_debugLog);
        page.Controls.Add(buttons);
        return page;
    }

    // Tail only while the Debug tab is actually visible (mac parity, ~4×/s).
    void UpdateLogTailing()
    {
        bool debugVisible = _tabs.SelectedTab?.Text == "Debug" && _existing != null;
        if (debugVisible) { _logLength = -1; _tailTimer.Start(); TailLog(); }
        else _tailTimer.Stop();
    }

    void TailLog()
    {
        if (_existing == null) return;
        var path = Paths.LogFile(_existing);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { if (_logLength != 0) { _debugLog.Text = ""; _logLength = 0; } return; }
            if (info.Length == _logLength) return;
            _logLength = info.Length;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const int cap = 64 * 1024;   // show at most the last 64 KB
            if (fs.Length > cap) fs.Seek(-cap, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            _debugLog.Text = text;
            _debugLog.SelectionStart = _debugLog.TextLength;
            _debugLog.ScrollToCaret();
        }
        catch (IOException) { /* transient share conflict; retry next tick */ }
    }

    void ClearLog()
    {
        if (_existing == null) return;
        try
        {
            // The service's writer holds the file open (FileShare.Read), so
            // truncation can fail while connected — phase 3 adds a clear-log
            // op to the pipe so the writer truncates its own file.
            File.WriteAllText(Paths.LogFile(_existing), "");
            _logLength = -1;
            TailLog();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Can't clear the log while its tunnel is connected.",
                            "VpncBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
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

    // inField: accessory docked inside the field's right edge (e.g. the eye).
    static void AddRow(TableLayoutPanel t, string label, Control c, Control? extra = null, Control? inField = null)
    {
        if (c is TextBox tb) c = new FieldPanel(tb, inField);   // borderless field chrome
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 7, 3, 0) }, 0, row);
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

    DotsButton BrowseButton(TextBox tb, string title)
    {
        var b = new DotsButton();
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
        AddRow(v, "Group secret", _secret, inField: new EyeButton(_secret));
        AddRow(v, "Username", _username);
        AddRow(v, "Password", _password, inField: new EyeButton(_password));
        AddRow(v, "VPN domains", _domains);
        _domains.PlaceholderText = "example.com, corp.local";
        AddRow(v, "IKE Authmode", _authmode);
        AddRow(v, "CA file", _caFile, inField: BrowseButton(_caFile, "Choose CA certificate"));
        AddRow(v, "Client cert", _clientCert, inField: BrowseButton(_clientCert, "Choose client certificate"));
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
        AddRow(o, "Password", ocPassword, inField: new EyeButton(ocPassword));
        AddRow(o, "VPN domains", ocDomains);
        AddRow(o, "Client cert", ocClientCert, inField: BrowseButton(ocClientCert, "Choose client certificate"));
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
        AddRow(v, "Forward Secrecy", _pfs);
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

        void Sel(ThemedCombo cb, string? v) { if (Profile.Ne(v) is string s && cb.Items.Contains(s)) cb.SelectedItem = s; }
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
        static string? Def(ThemedCombo cb, string def)
        {
            var v = cb.SelectedItem;
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
