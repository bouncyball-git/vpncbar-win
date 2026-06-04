using System.Text.Json;

namespace VpncBar.Ipc;

// Wire protocol between the tray app and the service: one JSON line request,
// one JSON line response, over \\.\pipe\vpncbar. The tray sends profile
// identity + the stdin payload (config/secrets — never persisted service-side);
// the service decides the actual binaries and argv (fixed installed paths
// only, mirroring the mac sudoers rule pinning exact binaries).
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
    string? Kind = null,       // "vpnc" | "openconnect" (backend selection; stubbed in phase 2)
    string? Stdin = null);     // config/secret payload piped to the child, never stored

record TunnelInfo(string Uuid, int Pid, long SinceUnix);

record PipeResponse(bool Ok, string? Error = null, List<TunnelInfo>? Tunnels = null);
