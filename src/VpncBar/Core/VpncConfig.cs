using System.Net;
using System.Net.Sockets;

namespace VpncBar.Core;

// Builds the vpnc.conf text piped to vpnc.exe on stdin — a direct port of the
// macOS app's connect() config builder. Secrets come from the Credential
// Manager (tray-side; the LocalSystem service can't read them). The "Script"
// directive is NOT included here — the service appends it, since only the
// service knows its own elevated exe path (security model: clients never name
// executables). VPNC_MATCH_DOMAINS / VPNCBAR_UUID are passed as process
// environment by the service (Windows has no /bin/sh env-prefix on the Script
// line like macOS used).
static class VpncConfig
{
    public sealed record Built(string ConfigText, string? Error);

    public static Built Build(Profile p)
    {
        var authmode = Profile.Ne(p.Authmode) ?? "psk";
        bool usesCert = authmode is "cert" or "hybrid";
        var (xauthDomain, xauthUser) = Profile.SplitDomainUser(p.Username);

        var lines = new List<string>
        {
            $"IPSec gateway {ResolveGatewayIP(p.Gateway)}",
            $"IPSec ID {p.Id}",
            $"IKE Authmode {authmode}",
            $"Xauth username {xauthUser}",
        };

        if (usesCert)
        {
            var ca = Profile.Ne(p.CaFile);
            if (ca == null) return new("", $"{authmode} auth needs a CA file.\nOpen the profile and set it.");
            lines.Add($"CA-File {ca}");
        }
        else
        {
            var secret = ProfileStore.Secret(p);
            if (secret == null) return new("", $"Group secret not found for “{p.Name}”.\nOpen the profile and set it.");
            lines.Add($"IPSec secret {secret}");
        }

        var password = ProfileStore.Password(p);
        if (xauthDomain != null) lines.Add($"Domain {xauthDomain}");
        if (password != null) lines.Add($"Xauth password {password}");

        void Add(string key, string? value)
        {
            if (Profile.Ne(value) is { } v) lines.Add($"{key} {v}");
        }
        Add("IKE DH Group", p.DhGroup);
        Add("Perfect Forward Secrecy", p.Pfs);
        Add("NAT Traversal Mode", p.NatMode);
        Add("Vendor", p.Vendor);
        Add("Interface MTU", p.Mtu);
        Add("DPD idle timeout (our side)", p.DpdTimeout);
        Add("Debug", p.Debug);
        // Interface mode left default (tun → Wintun). App version / Local Addr /
        // Local Port / UDP Encap Port left to vpnc's automatic defaults.
        if (p.EnableWeak ?? true) lines.Add("Enable weak encryption");   // defaults ON (mac parity)
        if (p.SingleDES ?? false) lines.Add("Enable Single DES");
        if (p.NoEncryption ?? false) lines.Add("Enable no encryption");
        if (p.WeakAuth ?? false) lines.Add("Enable weak authentication");
        if (p.Extra != null) lines.AddRange(p.Extra);

        return new(string.Join("\n", lines) + "\n", null);
    }

    // Display-only argv for the Info tab (no secrets — they're on stdin).
    public static string CommandLine() =>
        "vpnc --non-inter --log-file <session.log> -   (config on stdin)";

    // Last good gateway-hostname → IP, so a reconnect still works even if a
    // stale scoped resolver would route the gateway into the VPN DNS. Port of
    // the mac resolveGatewayIP() + gatewayIPCache.
    static readonly Dictionary<string, string> _gatewayIPCache = [];

    public static string ResolveGatewayIP(string host)
    {
        if (IPAddress.TryParse(host, out var literal) && literal.AddressFamily == AddressFamily.InterNetwork)
            return host;   // already an IPv4 literal
        try
        {
            var ip = Dns.GetHostAddresses(host)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null)
            {
                _gatewayIPCache[host] = ip.ToString();
                return ip.ToString();
            }
        }
        catch (Exception e) when (e is SocketException or ArgumentException) { }
        return _gatewayIPCache.TryGetValue(host, out var cached) ? cached : host;   // last good, else hostname
    }
}
