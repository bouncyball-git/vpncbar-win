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

    // Tie the service's lifetime to this tray process (the service watches the
    // PID and stops when we exit — see OwnerWatcher). Called once at launch.
    public static void RegisterOwner()
    {
        Call(new("own", Pid: Environment.ProcessId), 2000);
    }

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
        string kind;
        string? stdin = null;
        OcOptions? oc = null;

        if (p.IsOpenconnect)
        {
            kind = "openconnect";
            // openconnect reads one value per form prompt from stdin: the
            // account password, then (for 2FA groups) the one-time code.
            stdin = (ProfileStore.Password(p) ?? "") + "\n";
            if (!string.IsNullOrWhiteSpace(otp)) stdin += otp.Trim() + "\n";
            oc = new OcOptions(
                Gateway: p.Gateway,
                Username: Profile.SplitDomainUser(p.Username).User,
                Authgroup: p.OcAuthgroup,
                ServerCert: p.OcServerCert,
                Protocol: p.OcProtocol,
                NoDtls: p.OcNoDTLS ?? false,
                Dpd: p.OcDPD,
                Mtu: p.OcMTU,
                Reconnect: p.OcReconnect,
                Debug: p.OcDebug,
                ClientCert: p.ClientCert,
                MatchDomains: p.DnsMatchDomains);
        }
        else if (p.Gateway == "stub")
        {
            kind = "stub";   // dev backend (gateway literally "stub")
        }
        else
        {
            kind = "vpnc";
            // The tray builds the vpnc.conf (it can read the Credential Manager;
            // the service can't). The service appends only the Script directive.
            var built = VpncConfig.Build(p);
            if (built.Error != null) return built.Error;
            stdin = built.ConfigText;
        }

        var req = new PipeRequest("connect",
            Uuid: p.Uuid ?? p.Name,
            Name: p.Name,
            Kind: kind,
            Stdin: stdin,
            Oc: oc,
            MatchDomains: p.IsOpenconnect ? null : p.DnsMatchDomains);
        var resp = Call(req, 20000);   // IKE / SSL auth can take a while
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

    // Display-only: the argv the service launches for this profile (the mac
    // vpncCommandLine, shown in the Info tab). Secrets ride stdin, never argv.
    // Mirrors TunnelManager.OpenconnectPsi — keep the two in sync.
    public static string CommandLine(Profile p)
    {
        if (!p.IsOpenconnect) return VpncConfig.CommandLine();
        var a = new List<string>
        {
            "openconnect",
            $"--protocol={Profile.Ne(p.OcProtocol) ?? "anyconnect"}",
            "--passwd-on-stdin",
            $"--user={Profile.SplitDomainUser(p.Username).User}",
            "--script vpncbar-script.js",
            $"--interface vpncbar-{(p.Uuid ?? p.Name)[..Math.Min(8, (p.Uuid ?? p.Name).Length)]}",
        };
        switch (Profile.Ne(p.OcDebug) ?? "1")
        {
            case "1": a.Add("-v"); break;
            case "2": a.Add("-vv"); break;
            case "3": a.Add("-vvv"); break;
            case "99": a.Add("-vvv --dump-http-traffic"); break;
        }
        if (p.OcNoDTLS ?? false) a.Add("--no-dtls");
        if (Profile.Ne(p.OcDPD) is { } dpd) a.Add($"--dpd {dpd}");
        if (Profile.Ne(p.OcMTU) is { } mtu) a.Add($"--mtu {mtu}");
        if (Profile.Ne(p.OcReconnect) is { } rc) a.Add($"--reconnect-timeout {rc}");
        if (Profile.Ne(p.OcAuthgroup) is { } g) a.Add($"--authgroup={g}");
        if (Profile.Ne(p.OcServerCert) is { } pin) a.Add($"--servercert={pin}");
        if (Profile.Ne(p.ClientCert) is { } cert) a.Add($"--certificate={cert}");
        a.Add(p.Gateway);
        return string.Join(" ", a);
    }
}
