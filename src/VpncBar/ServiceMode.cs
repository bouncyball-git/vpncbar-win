using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VpncBar.Service;

namespace VpncBar;

// Privileged engine (LocalSystem, Session 0): tunnel manager, pipe server.
// `--service` under the SCM is the normal case; `--service --console` runs
// the same engine inline for development (stop with Ctrl+C / Stop-Process —
// the engine logs to %ProgramData%\VpncBar\service.log either way).
static class ServiceMode
{
    public static int Run(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();   // our own file log; no console in a WinExe
        builder.Services.AddWindowsService(o => o.ServiceName = ServiceInstaller.ServiceName);
        builder.Services.AddHostedService<EngineHost>();
        builder.Build().Run();
        return 0;
    }

    sealed class EngineHost : IHostedService
    {
        readonly ServiceEngine _engine = new();

        public Task StartAsync(CancellationToken ct)
        {
            _engine.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            _engine.Stop();   // disconnect-all on service stop / shutdown
            return Task.CompletedTask;
        }
    }
}
