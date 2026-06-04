using System.IO.Pipes;
using System.Text.Json;
using VpncBar.Core;

namespace VpncBar.Ipc;

// Tray-side client for the service's named pipe: one JSON line out, one back.
// Connect sends the profile identity + stdin payload (config/secrets read
// tray-side — the LocalSystem service can't reach the user's Credential
// Manager, and must never persist secrets anyway).
static class TunnelClient
{
    const string NoService =
        "The VpncBar service isn't running.\n\n" +
        "Install it once from an elevated terminal:\n" +
        "    VpncBar.exe --install-service";

    static PipeResponse? Call(PipeRequest req, int timeoutMs)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut);
            pipe.Connect(timeoutMs);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(req, PipeProtocol.Json));
            var line = reader.ReadLine();
            return line == null ? null : JsonSerializer.Deserialize<PipeResponse>(line, PipeProtocol.Json);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;   // service not running / unreachable
        }
    }

    public static bool ServiceAvailable => Call(new("status"), 250) != null;

    // Connected profiles → connected-since, keyed by profile name (what the
    // menu displays). Short timeout: this runs on the 5s poll and menu-open.
    public static Dictionary<string, DateTime> Status(IReadOnlyList<Profile> profiles)
    {
        var result = new Dictionary<string, DateTime>();
        var resp = Call(new("status"), 250);
        if (resp?.Tunnels == null) return result;
        var byUuid = profiles.Where(p => p.Uuid != null).ToDictionary(p => p.Uuid!, p => p.Name);
        foreach (var t in resp.Tunnels)
        {
            if (byUuid.TryGetValue(t.Uuid, out var name))
                result[name] = DateTimeOffset.FromUnixTimeSeconds(t.SinceUnix).LocalDateTime;
        }
        return result;
    }

    // null = success; otherwise a user-facing error message (mac ActionResult).
    public static string? Connect(Profile p, string? otp = null)
    {
        // Phase 3 builds the real stdin payload here (vpnc config text with
        // secrets from Credential Manager / openconnect password + OTP).
        var req = new PipeRequest("connect",
            Uuid: p.Uuid ?? p.Name,
            Name: p.Name,
            Kind: p.IsOpenconnect ? "openconnect" : "vpnc");
        var resp = Call(req, 5000);
        if (resp == null) return NoService;
        return resp.Ok ? null : resp.Error ?? "connect failed";
    }

    public static string? Disconnect(Profile p)
    {
        var resp = Call(new("disconnect", Uuid: p.Uuid ?? p.Name), 10000);
        if (resp == null) return NoService;
        return resp.Ok ? null : resp.Error ?? "disconnect failed";
    }

    public static string? DisconnectAll()
    {
        var resp = Call(new("disconnect-all"), 15000);
        // Quitting with no service running is fine — nothing to tear down.
        return resp == null || resp.Ok ? null : resp.Error;
    }
}
