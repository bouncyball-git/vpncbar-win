using VpncBar.Tray;

namespace VpncBar;

// One executable, multiple modes:
//   VpncBar.exe                → tray app (per-user, unprivileged)
//   VpncBar.exe --service      → Windows service (LocalSystem)     [phase 2]
//   VpncBar.exe --script       → network-config shim for backends  [phase 3]
//   VpncBar.exe --install-service / --uninstall-service            [phase 2]
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "--service":
                return ServiceMode.Run(args);
            case "--script":
                return ScriptMode.Run(args);
            case "--install-service":
                return Service.ServiceInstaller.Install();
            case "--uninstall-service":
                return Service.ServiceInstaller.Uninstall();
            case "--enable-autostart":   // installer runs this as the original user (HKCU Run)
                Core.AutoStart.SetEnabled(true);
                return 0;
            case "--make-icon":   // dev aid: regenerate assets/VpncBar.ico from the SVG art
                return Tray.TrayIcons.WriteIco(args.ElementAtOrDefault(1) ?? "VpncBar.ico");
            case "--ui-demo":   // dev aid: open the profile editor directly (UI iteration/screenshots)
                ApplicationConfiguration.Initialize();
                Application.SetColorMode(SystemColorMode.System);
                Application.Run(new Tray.ProfileEditorForm(null, () => { }));
                return 0;
            default:
                return RunTray();
        }
    }

    static int RunTray()
    {
        // Single instance per session (the mac app checks running bundle ids).
        using var mutex = new Mutex(initiallyOwned: true, @"Local\VpncBar-tray", out bool createdNew);
        if (!createdNew) return 0;

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);   // follow the system light/dark theme
        Application.Run(new TrayContext());
        return 0;
    }
}
