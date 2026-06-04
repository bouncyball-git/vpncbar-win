using VpncBar.Core;

namespace VpncBar.Service;

// The privileged engine: tunnel manager + pipe server lifecycle. Hosted by
// the Windows service (normal case) or run inline by `--service --console`
// for development. Stop() tears down every tunnel — the service-stop /
// shutdown half of the "never orphan tunnels" guarantee.
sealed class ServiceEngine
{
    readonly CancellationTokenSource _cts = new();
    TunnelManager? _manager;
    Task? _serverTask;

    public void Start()
    {
        Directory.CreateDirectory(Paths.RunDir);
        // No tunnel can be up at service start (children die with the
        // service), so any VpncBar NRPT rule is an orphan from a crash.
        NetConfig.SweepNrptRules(Log);
        _manager = new TunnelManager(Log);
        _serverTask = new PipeServer(_manager, Log).RunAsync(_cts.Token);
        Log("service started");
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
