using System.Diagnostics;

namespace VpncBar.Service;

// Ties the service's lifetime to the tray's (per the "always stop with tray"
// model): the tray registers its PID via the pipe ("own"), and when that
// process exits — clean quit, logoff, OR crash/kill, all indistinguishable
// to a process-exit wait — the service tears everything down and stops.
//
// A grace timer covers the case where the tray starts the service but never
// registers (e.g. it crashed in the gap): with no owner after the grace
// window, the service stops itself rather than linger forever.
sealed class OwnerWatcher(Action onOrphaned, Action<string> log)
{
    readonly object _lock = new();
    readonly HashSet<int> _owners = [];
    bool _everOwned;
    bool _fired;

    public void StartGraceTimer()
    {
        // If no tray claims ownership within 60s of start, assume an orphaned
        // service (tray died before registering) and shut down.
        Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ =>
        {
            lock (_lock)
            {
                if (_everOwned || _fired) return;
            }
            log("no tray claimed ownership within 60s — stopping orphaned service");
            Fire();
        });
    }

    public void Own(int pid)
    {
        Process proc;
        try { proc = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }   // already gone

        lock (_lock)
        {
            if (!_owners.Add(pid)) return;
            _everOwned = true;
        }
        log($"owner registered: tray pid {pid}");

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            bool orphaned;
            lock (_lock)
            {
                _owners.Remove(pid);
                orphaned = _owners.Count == 0;
            }
            log($"owner tray pid {pid} exited");
            if (orphaned) Fire();
        };
        // Cover the race where it exited between GetProcessById and the hook.
        if (proc.HasExited)
        {
            lock (_lock) { _owners.Remove(pid); }
            Fire();
        }
    }

    void Fire()
    {
        lock (_lock)
        {
            if (_fired) return;
            _fired = true;
        }
        onOrphaned();
    }
}
