using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using VpncBar.Ipc;

namespace VpncBar.Service;

// Named-pipe server: one JSON line in, one JSON line out, then disconnect.
// ACL: Authenticated Users read/write — any interactive user may operate
// tunnels, the same threat model as the mac NOPASSWD sudoers rule. The
// service only ever executes its own fixed binaries (see TunnelManager).
sealed class PipeServer(TunnelManager manager, Action<string> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            using var server = NamedPipeServerStreamAcl.Create(
                PipeProtocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0, security);
            try
            {
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, leaveOpen: true);
                await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(ct);
                if (line == null) continue;
                var response = Handle(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, PipeProtocol.Json));
                server.WaitForPipeDrain();
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { /* client vanished mid-request; next loop */ }
        }
    }

    PipeResponse Handle(string line)
    {
        PipeRequest? req;
        try { req = JsonSerializer.Deserialize<PipeRequest>(line, PipeProtocol.Json); }
        catch (JsonException e) { return new(false, $"bad request: {e.Message}"); }
        if (req == null) return new(false, "bad request");

        return req.Op switch
        {
            "connect" => manager.Connect(req),
            "disconnect" => manager.Disconnect(req.Uuid),
            "disconnect-all" => manager.DisconnectAll(),
            "status" => manager.Status(),
            _ => new(false, $"unknown op '{req.Op}'"),
        };
    }
}
