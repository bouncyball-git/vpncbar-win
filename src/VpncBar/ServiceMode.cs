using System.ServiceProcess;
using VpncBar.Service;

namespace VpncBar;

// Privileged engine (LocalSystem, Session 0): tunnel manager, pipe server.
// Under the SCM this runs as a Windows service (ServiceBase); started from a
// terminal (`--service --console` or no SCM) the same engine runs inline for
// development. Logs to %ProgramData%\VpncBar\service.log either way.
static class ServiceMode
{
    public static int Run(string[] args)
    {
        if (Environment.UserInteractive || args.Contains("--console"))
        {
            // Dev mode: engine inline, runs until the process is stopped.
            var engine = new ServiceEngine();
            using var idle = new ManualResetEventSlim(false);
            engine.Start(requestStop: () => idle.Set());
            AppDomain.CurrentDomain.ProcessExit += (_, _) => engine.Stop();
            idle.Wait();   // released when the owner tray is gone
            engine.Stop();
            return 0;
        }

        ServiceBase.Run(new VpncBarService());
        return 0;
    }

    sealed class VpncBarService : ServiceBase
    {
        readonly ServiceEngine _engine = new();

        public VpncBarService()
        {
            ServiceName = ServiceInstaller.ServiceName;
            CanShutdown = true;   // OnShutdown → tunnels torn down on system shutdown
        }

        // requestStop runs Stop() through the SCM, so OnStop does the teardown.
        protected override void OnStart(string[] args) => _engine.Start(requestStop: Stop);
        protected override void OnStop() => _engine.Stop();
        protected override void OnShutdown() => _engine.Stop();
    }
}
