using System.Collections.Concurrent;
using System.Diagnostics;
using VpncBar.Core;
using VpncBar.Ipc;

namespace VpncBar.Service;

// The service's tunnel table: one child process per connected profile, keyed
// by profile uuid. State is authoritative here (no pidfile parsing like the
// mac app needed). Each child's stdout/stderr is redirected to the
// per-profile session log, truncated per connect — the Debug tab tails it.
//
// Phase 2: every backend kind maps to a harmless STUB child (ping -t) that
// produces periodic output, so the whole connect/disconnect/status/log loop
// can be exercised before the real backends exist (phases 3/4).
sealed class TunnelManager(Action<string> log)
{
    sealed record Entry(Process Proc, string Name, DateTime Since, string LogFile);

    readonly ConcurrentDictionary<string, Entry> _tunnels = new();

    public PipeResponse Connect(PipeRequest r)
    {
        if (r.Uuid is not { Length: > 0 } uuid) return new(false, "connect: missing uuid");
        var name = r.Name ?? uuid;
        if (_tunnels.ContainsKey(uuid)) return new(true);   // already up — never double-connect

        Directory.CreateDirectory(Paths.RunDir);
        var logFile = Paths.LogFile(uuid, name);

        // The service decides the executable — clients can never pick a path.
        // Phase 2 stub: ping -t (long-running, periodic output, killable).
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "PING.EXE"),
            Arguments = "-t 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = r.Stdin != null,
        };

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

        // Config/secrets ride stdin and are never persisted (mac parity).
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

        var entry = new Entry(proc, name, DateTime.Now, logFile);
        _tunnels[uuid] = entry;

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            _tunnels.TryRemove(uuid, out _);
            try { writer.Dispose(); } catch (ObjectDisposedException) { }
            log($"tunnel {name}: child exited (code {proc.ExitCode})");
        };

        log($"connect {name}: child pid {proc.Id}, log {Path.GetFileName(logFile)}");
        return new(true);
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
            // Phase 2: hard kill. Real backends get a graceful stop (vpnc: named
            // stop event running the teardown script; openconnect: SIGTERM
            // equivalent) so routes/DNS are restored — see PORTING.md §5.
            entry.Proc.Kill(entireProcessTree: true);
            entry.Proc.WaitForExit(5000);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone
        }
        _tunnels.TryRemove(uuid, out _);
        log($"disconnect {entry.Name}");
        return new(true);
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
}
