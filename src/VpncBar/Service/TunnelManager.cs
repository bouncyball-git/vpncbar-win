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
    sealed record Entry(Process Proc, string Name, DateTime Since, string LogFile);

    readonly ConcurrentDictionary<string, Entry> _tunnels = new();

    public PipeResponse Connect(PipeRequest r)
    {
        if (r.Uuid is not { Length: > 0 } uuid) return new(false, "connect: missing uuid");
        var name = r.Name ?? uuid;
        if (_tunnels.ContainsKey(uuid)) return new(true);   // already up — never double-connect

        ProcessStartInfo psi;
        switch (r.Kind)
        {
            case "openconnect":
                if (!Backends.HasOpenconnect)
                    return new(false, "The openconnect backend isn't bundled with this build.\n(vendor/openconnect/bin is missing — see tools/fetch-openconnect.ps1.)");
                if (r.Oc is not { Gateway.Length: > 0 })
                    return new(false, "connect: missing openconnect options");
                psi = OpenconnectPsi(uuid, r.Oc);
                break;
            case "stub":   // dev backend: long-running, periodic output, killable
                psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "PING.EXE"),
                    Arguments = "-t 127.0.0.1",
                };
                break;
            case "vpnc":
                return new(false, "Cisco IPSec (vpnc) tunnels arrive in phase 4.\nThis build can connect openconnect (AnyConnect) profiles.");
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

        // Secrets ride stdin and are never persisted (mac parity).
        if (r.Stdin is { } stdin)
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

        _tunnels[uuid] = new Entry(proc, name, DateTime.Now, logFile);

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
            // Hard kill, then the service cleans the network config itself —
            // teardown never depends on the child cooperating. (A graceful
            // stop signal can come later for cleaner gateway logouts.)
            entry.Proc.Kill(entireProcessTree: true);
            entry.Proc.WaitForExit(5000);
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
}
