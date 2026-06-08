using VpncBar.Core;

namespace VpncBar.Service;

// The privileged engine: tunnel manager + pipe server + owner watcher.
// Lifetime is tied to the tray (the "always stop with tray" model): when the
// owning tray process exits for any reason, the watcher fires requestStop,
// which tears down all tunnels and stops the service.
sealed class ServiceEngine
{
    readonly CancellationTokenSource _cts = new();
    TunnelManager? _manager;
    OwnerWatcher? _watcher;
    Task? _serverTask;
    Action? _requestStop;

    // requestStop: ask the host (Windows service / console) to stop us — the
    // SCM stop then runs Stop() below (disconnect-all + cleanup).
    public void Start(Action requestStop)
    {
        _requestStop = requestStop;
        Directory.CreateDirectory(Paths.RunDir);
        NetConfig.SweepNrptRules(Log);   // clear any orphan rules from a crash
        _manager = new TunnelManager(Log);
        _watcher = new OwnerWatcher(onOrphaned: OnOrphaned, Log);
        _watcher.StartGraceTimer();
        _serverTask = new PipeServer(_manager, _watcher, Log).RunAsync(_cts.Token);
        Log("service started");
    }

    void OnOrphaned()
    {
        // The owning tray is gone — disconnect everything, then ask to stop.
        _manager?.DisconnectAll();
        _requestStop?.Invoke();
    }

    public void Stop()
    {
        Log("service stopping — disconnecting all tunnels");
        _manager?.DisconnectAll();
        _cts.Cancel();
        try { _serverTask?.Wait(3000); } catch (AggregateException) { }
        Log("service stopped");
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Paths.ProgramDataDir);
            File.AppendAllText(Paths.ServiceLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\r\n");
        }
        catch (Exception) { /* logging must never take the service down */ }
    }
}
