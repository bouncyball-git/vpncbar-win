using System.Text.Json.Serialization;

namespace VpncBar.Core;

// VPN profile. The JSON property names match the macOS app's Swift Codable
// encoding exactly, so a profiles.json moved between the two apps just works
// (secrets don't transfer — Keychain vs Credential Manager).
public class Profile
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }          // stable identity; creds key off this
    [JsonPropertyName("name")] public string Name { get; set; } = "";     // display label
    [JsonPropertyName("gateway")] public string Gateway { get; set; } = ""; // IPSec gateway / openconnect server
    [JsonPropertyName("id")] public string Id { get; set; } = "";         // IPSec ID (group name)
    [JsonPropertyName("username")] public string Username { get; set; } = ""; // Xauth username

    // Optional vpnc options. null/"" => directive omitted (vpnc default).
    [JsonPropertyName("authmode")] public string? Authmode { get; set; }   // IKE Authmode: psk/cert/hybrid
    [JsonPropertyName("dhGroup")] public string? DhGroup { get; set; }     // IKE DH Group
    [JsonPropertyName("pfs")] public string? Pfs { get; set; }             // Perfect Forward Secrecy
    [JsonPropertyName("natMode")] public string? NatMode { get; set; }     // NAT Traversal Mode
    [JsonPropertyName("vendor")] public string? Vendor { get; set; }       // cisco/netscreen/fortigate
    [JsonPropertyName("ifmode")] public string? Ifmode { get; set; }       // tun/tap
    [JsonPropertyName("domain")] public string? Domain { get; set; }       // auth/NT domain
    [JsonPropertyName("dnsMatchDomains")] public string? DnsMatchDomains { get; set; } // scoped-DNS match domains
    [JsonPropertyName("caFile")] public string? CaFile { get; set; }       // CA cert path (cert/hybrid)
    [JsonPropertyName("clientCert")] public string? ClientCert { get; set; } // client cert path
    [JsonPropertyName("appVersion")] public string? AppVersion { get; set; }
    [JsonPropertyName("localAddr")] public string? LocalAddr { get; set; }
    [JsonPropertyName("localPort")] public string? LocalPort { get; set; }
    [JsonPropertyName("udpPort")] public string? UdpPort { get; set; }     // Cisco UDP Encapsulation Port
    [JsonPropertyName("mtu")] public string? Mtu { get; set; }             // Interface MTU
    [JsonPropertyName("dpdTimeout")] public string? DpdTimeout { get; set; } // DPD idle timeout (our side)
    [JsonPropertyName("debug")] public string? Debug { get; set; }         // 0/1/2/3/99
    [JsonPropertyName("enableWeak")] public bool? EnableWeak { get; set; } // weak encryption (3DES) — defaults on
    [JsonPropertyName("singleDES")] public bool? SingleDES { get; set; }
    [JsonPropertyName("noEncryption")] public bool? NoEncryption { get; set; }
    [JsonPropertyName("weakAuth")] public bool? WeakAuth { get; set; }
    [JsonPropertyName("extra")] public List<string>? Extra { get; set; }   // verbatim vpnc.conf directives

    // Backend: null/"vpnc" => Cisco IPSec via bundled vpnc; "openconnect" => AnyConnect
    // SSL. For openconnect we reuse Gateway(=server), Username, password,
    // DnsMatchDomains, ClientCert.
    [JsonPropertyName("kind")] public string? Kind { get; set; }           // "vpnc" (default) | "openconnect"
    [JsonPropertyName("ocAuthgroup")] public string? OcAuthgroup { get; set; }   // --authgroup
    [JsonPropertyName("ocServerCert")] public string? OcServerCert { get; set; } // --servercert pin
    [JsonPropertyName("ocOtp")] public bool? OcOtp { get; set; }           // prompt for one-time 2FA code
    [JsonPropertyName("ocProtocol")] public string? OcProtocol { get; set; } // anyconnect/gp/pulse/f5/fortinet/nc/array
    [JsonPropertyName("ocNoDTLS")] public bool? OcNoDTLS { get; set; }     // --no-dtls
    [JsonPropertyName("ocDPD")] public string? OcDPD { get; set; }         // --dpd seconds
    [JsonPropertyName("ocMTU")] public string? OcMTU { get; set; }         // --mtu
    [JsonPropertyName("ocReconnect")] public string? OcReconnect { get; set; } // --reconnect-timeout seconds
    [JsonPropertyName("ocDebug")] public string? OcDebug { get; set; }     // verbosity 0/1/2/3/99

    [JsonIgnore] public bool IsOpenconnect => (Kind ?? "vpnc") == "openconnect";

    // Trimmed non-empty value, or null (the mac app's ne()).
    public static string? Ne(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    // Split "DOMAIN\user" into (domain, user); a plain "user" yields (null, user).
    public static (string? Domain, string User) SplitDomainUser(string s)
    {
        int i = s.IndexOf('\\');
        if (i < 0) return (null, s);
        var d = s[..i].Trim();
        var u = s[(i + 1)..].Trim();
        return (d.Length == 0 ? null : d, u);
    }
}
