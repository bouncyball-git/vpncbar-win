using System.ServiceProcess;

namespace VpncBar.Ipc;

// Tray-side control of the Windows service's lifetime. The service is
// demand-start and its security descriptor grants Authenticated Users
// start/stop (set by --install-service), so the non-elevated tray can bring
// it up on launch. It then registers ownership over the pipe so the service
// stops when the tray exits (see OwnerWatcher) — explicit Stop() here just
// makes a clean quit instant.
static class ServiceControl
{
    const string Name = "VpncBar";

    public static bool IsInstalled()
    {
        try
        {
            using var sc = new ServiceController(Name);
            _ = sc.Status;   // throws if not installed
            return true;
        }
        catch (Exception) { return false; }
    }

    // Start the service if installed and not already running. Best-effort:
    // returns true once it's running, false if absent or it couldn't start.
    public static bool EnsureRunning()
    {
        try
        {
            using var sc = new ServiceController(Name);
            if (sc.Status is ServiceControllerStatus.Running) return true;
            if (sc.Status is ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return sc.Status == ServiceControllerStatus.Running;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (Exception) { return false; }
    }

    public static void Stop()
    {
        try
        {
            using var sc = new ServiceController(Name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        }
        catch (Exception) { /* not installed / no rights / already gone */ }
    }
}
