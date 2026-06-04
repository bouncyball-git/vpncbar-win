using System.Text.Json;

namespace VpncBar.Ipc;

// Wire protocol between the tray app and the service: one JSON line request,
// one JSON line response, over \\.\pipe\vpncbar. The tray sends profile
// identity, validated options and the stdin payload (config/secrets — never
// persisted service-side); the service decides the actual binaries and argv
// (fixed installed paths only, mirroring the mac sudoers rule pinning exact
// binaries).
static class PipeProtocol
{
    public const string PipeName = "vpncbar";

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

record PipeRequest(
    string Op,                 // "connect" | "disconnect" | "disconnect-all" | "status"
    string? Uuid = null,       // profile identity (connect/disconnect)
    string? Name = null,       // display name (log file naming, messages)
    string? Kind = null,       // "openconnect" | "vpnc" (phase 4) | "stub" (dev)
    string? Stdin = null,      // secret payload piped to the child, never stored
    OcOptions? Oc = null);     // openconnect options (validated values, no paths to binaries)

// openconnect profile options — the service maps these onto a fixed argv
// template; a client can never inject arbitrary arguments or binary paths.
record OcOptions(
    string Gateway,
    string Username,
    string? Authgroup = null,
    string? ServerCert = null,     // pin-sha256:…
    string? Protocol = null,       // anyconnect (default) /gp/pulse/f5/fortinet/nc/array
    bool NoDtls = false,
    string? Dpd = null,
    string? Mtu = null,
    string? Reconnect = null,
    string? Debug = null,          // 0/1/2/3/99
    string? ClientCert = null,
    string? MatchDomains = null);  // scoped-DNS domains from the profile

record TunnelInfo(string Uuid, int Pid, long SinceUnix);

record PipeResponse(bool Ok, string? Error = null, List<TunnelInfo>? Tunnels = null);
