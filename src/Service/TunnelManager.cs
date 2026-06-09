using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VpncBar.Core;
using VpncBar.Ipc;

namespace VpncBar.Service;

// The service's tunnel table: one child process per connected profile, keyed
// by profile uuid. State is authoritative here. Each child's stdout/stderr is
// redirected to the per-profile session log, truncated per connect — the
// Debug tab tails it. The service alone decides which executables run
// (fixed bundled paths) — clients send options, never paths.
//
// Backends: "openconnect" = bundled openconnect.exe (+ wintun.dll beside it);
// "vpnc" arrives in phase 4; "stub" (ping) is kept for development.
sealed class TunnelManager(Action<string> log)
{
    sealed record Entry(Process Proc, string Name, DateTime Since, string LogFile, string? StopEvent);

    readonly ConcurrentDictionary<string, Entry> _tunnels = new();

    public PipeResponse Connect(PipeRequest r)
    {
        if (r.Uuid is not { Length: > 0 } uuid) return new(false, "connect: missing uuid");
        var name = r.Name ?? uuid;
        if (_tunnels.ContainsKey(uuid)) return new(true);   // already up — never double-connect

        ProcessStartInfo psi;
        string? stopEvent = null;
        switch (r.Kind)
        {
            case "openconnect":
                if (!Backends.HasOpenconnect)
                    return new(false, "The openconnect backend isn't bundled with this build.\n(dist/backend is missing — see scripts/build-openconnect.ps1.)");
                if (r.Oc is not { Gateway.Length: > 0 })
                    return new(false, "connect: missing openconnect options");
                psi = OpenconnectPsi(uuid, r.Oc);
                break;
            case "vpnc":
                if (!Backends.HasVpnc)
                    return new(false, "The vpnc backend isn't bundled with this build.\n(dist/backend is missing — see scripts/build-vpnc.ps1.)");
                if (r.Stdin is not { Length: > 0 })
                    return new(false, "connect: missing vpnc config");
                stopEvent = $"Global\\vpncbar-stop-{uuid}";
                psi = VpncPsi(uuid, r.MatchDomains, stopEvent);
                break;
            case "stub":   // dev backend: long-running, periodic output, killable
                psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "PING.EXE"),
                    Arguments = "-t 127.0.0.1",
                };
                break;
            default:
                return new(false, $"unknown backend '{r.Kind}'");
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = r.Stdin != null;

        Directory.CreateDirectory(Paths.RunDir);
        var logFile = Paths.LogFile(uuid, name);

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception e)
        {
            log($"connect {name}: failed to start child: {e.Message}");
            return new(false, $"Failed to start tunnel process:\n{e.Message}");
        }

        // Secrets ride stdin and are never persisted (mac parity). For vpnc
        // the service appends the Script directive — only it knows its own
        // elevated exe path (clients never name executables).
        var payload = r.Kind == "vpnc"
            ? r.Stdin + $"Script \"{Environment.ProcessPath}\" --script\n"
            : r.Stdin;
        if (payload is { } stdin)
        {
            try
            {
                proc.StandardInput.Write(stdin);
                proc.StandardInput.Close();
            }
            catch (IOException) { /* child exited early; surfaced via Exited */ }
        }

        // Session log: both streams merged line-wise into one truncated file.
        var writer = new StreamWriter(new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
        PumpLines(proc.StandardOutput, writer);
        PumpLines(proc.StandardError, writer);

        _tunnels[uuid] = new Entry(proc, name, DateTime.Now, logFile, stopEvent);

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            _tunnels.TryRemove(uuid, out _);
            try { writer.Dispose(); } catch (ObjectDisposedException) { }
            CleanupNetwork(uuid);   // even an unexpected death never leaks NRPT rules
            log($"tunnel {name}: child exited (code {SafeExitCode(proc)})");
        };

        log($"connect {name} [{r.Kind}]: child pid {proc.Id}, log {Path.GetFileName(logFile)}");
        return new(true);
    }

    // Fixed argv template for the bundled openconnect — values come from the
    // profile, the shape is ours. Mirrors the mac app's openconnectArgs().
    ProcessStartInfo OpenconnectPsi(string uuid, OcOptions oc)
    {
        var psi = new ProcessStartInfo { FileName = Backends.OpenconnectExe };
        var a = psi.ArgumentList;
        a.Add($"--protocol={Ne(oc.Protocol) ?? "anyconnect"}");
        a.Add("--passwd-on-stdin");
        a.Add($"--user={oc.Username}");
        // openconnect wraps the script in `cscript.exe "<path>"` (script.c) —
        // only a .js works; it relays to "VpncBar.exe --script".
        a.Add("--script");
        a.Add(Backends.ScriptJs);
        a.Add("--interface");
        a.Add($"vpncbar-{uuid[..Math.Min(8, uuid.Length)]}");
        // Verbosity: 0 none · 1 -v · 2 -vv · 3 -vvv · 99 -vvv + full HTTP dump.
        switch (Ne(oc.Debug) ?? "1")
        {
            case "1": a.Add("-v"); break;
            case "2": a.Add("-vv"); break;
            case "3": a.Add("-vvv"); break;
            case "99": a.Add("-vvv"); a.Add("--dump-http-traffic"); break;
        }
        if (oc.NoDtls) a.Add("--no-dtls");
        if (Ne(oc.Dpd) is { } dpd) { a.Add("--dpd"); a.Add(dpd); }
        if (Ne(oc.Mtu) is { } mtu) { a.Add("--mtu"); a.Add(mtu); }
        if (Ne(oc.Reconnect) is { } rc) { a.Add("--reconnect-timeout"); a.Add(rc); }
        if (Ne(oc.Authgroup) is { } g) a.Add($"--authgroup={g}");
        if (Ne(oc.ServerCert) is { } pin) a.Add($"--servercert={pin}");
        if (Ne(oc.ClientCert) is { } cert) a.Add($"--certificate={cert}");
        a.Add(oc.Gateway);

        // The script (and any child) sees these: VPNCBAR_UUID pins the
        // .info/NRPT tag to the profile uuid (the mac app used VPNPID, but
        // openconnect overwrites VPNPID with its own pid on every platform —
        // mac dodged that via a /bin/sh env prefix that Windows doesn't have);
        // VPNC_MATCH_DOMAINS drives scoped DNS, same as macOS.
        psi.Environment["VPNCBAR_UUID"] = uuid;
        if (Ne(oc.MatchDomains) is { } raw)
        {
            var domains = string.Join(' ',
                Regex.Replace(raw, "[^A-Za-z0-9.\\-_, ]", "")
                    .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
            if (domains.Length > 0) psi.Environment["VPNC_MATCH_DOMAINS"] = domains;
        }
        return psi;
    }

    // vpnc.exe reads the full config (built tray-side) from stdin via "-".
    // The script env + graceful-stop event are passed as process environment
    // (Windows has no /bin/sh env-prefix on the Script line like macOS).
    ProcessStartInfo VpncPsi(string uuid, string? matchDomains, string stopEvent)
    {
        var psi = new ProcessStartInfo { FileName = Backends.VpncExe };
        psi.ArgumentList.Add("--non-inter");
        // Windows-only necessity. Windows' kernel IPsec demuxes inbound ESP-in-UDP
        // on port 4500, finds no kernel SA (vpnc's is in user space), and drops the
        // gateway's return ESP — so a vpnc NAT-T tunnel connects but passes no data.
        // vpnc's NAT-T float (vpnc.c) only rewrites the local port to 4500 when it's
        // the default 500; any other local port is kept while it still targets
        // gateway:4500. Binding a non-4500 local port makes the reply land on a dest
        // port Windows doesn't demux, so it reaches vpnc's socket untouched. "0" =
        // an OS-assigned ephemeral port: never 500/4500, unique per simultaneous
        // tunnel, and it also forces NAT-T (the only way userspace ESP is received
        // on Windows). The macOS app needs none of this; macOS has no such demux.
        psi.ArgumentList.Add("--local-port");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-");                          // config on stdin
        psi.Environment["VPNCBAR_UUID"] = uuid;             // --script: .info/NRPT tag
        psi.Environment["VPNCBAR_STOP_EVENT"] = stopEvent;  // graceful disconnect (SIGTERM analog)
        if (Ne(matchDomains) is { } raw)
        {
            var domains = string.Join(' ',
                Regex.Replace(raw, "[^A-Za-z0-9.\\-_, ]", "")
                    .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
            if (domains.Length > 0) psi.Environment["VPNC_MATCH_DOMAINS"] = domains;
        }
        return psi;
    }

    static string? Ne(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    static void PumpLines(StreamReader reader, StreamWriter writer)
    {
        Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lock (writer) writer.WriteLine(line);
                }
            }
            catch (Exception) { /* stream closed with the process */ }
        });
    }

    public PipeResponse Disconnect(string? uuid)
    {
        if (uuid == null || !_tunnels.TryGetValue(uuid, out var entry)) return new(true);   // not up
        try
        {
            // vpnc: signal its named stop event for a graceful teardown (sends
            // a delete-SA to the gateway, runs the disconnect script) — the
            // SIGTERM analog. Fall through to a hard kill if it doesn't exit.
            if (entry.StopEvent != null && SignalStopEvent(entry.StopEvent))
                entry.Proc.WaitForExit(5000);
            if (!entry.Proc.HasExited)
            {
                entry.Proc.Kill(entireProcessTree: true);
                entry.Proc.WaitForExit(5000);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone
        }
        _tunnels.TryRemove(uuid, out _);
        CleanupNetwork(uuid);
        log($"disconnect {entry.Name}");
        return new(true);
    }

    void CleanupNetwork(string uuid)
    {
        NetConfig.RemoveNrptRules(uuid, log);
        try { File.Delete(Path.Combine(Paths.RunDir, $"{uuid}.info")); } catch (IOException) { }
        // Split-include routes die with the Wintun adapter; NRPT is the part
        // that would otherwise leak.
    }

    public PipeResponse DisconnectAll()
    {
        foreach (var uuid in _tunnels.Keys.ToList()) Disconnect(uuid);
        return new(true);
    }

    public PipeResponse Status() => new(true, Tunnels:
        _tunnels.Select(kv => new TunnelInfo(
            kv.Key, SafePid(kv.Value.Proc), new DateTimeOffset(kv.Value.Since).ToUnixTimeSeconds())).ToList());

    static int SafePid(Process p)
    {
        try { return p.Id; } catch (InvalidOperationException) { return 0; }
    }

    static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch (InvalidOperationException) { return -1; }
    }

    // Open the vpnc child's named stop event and signal it. Returns false if
    // the event doesn't exist (vpnc not far enough along / already gone).
    static bool SignalStopEvent(string name)
    {
        try
        {
            using var ev = System.Threading.EventWaitHandle.OpenExisting(name);
            ev.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
