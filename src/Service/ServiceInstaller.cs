using System.Diagnostics;
using System.Security.Principal;

namespace VpncBar.Service;

// One-time service registration via sc.exe (the installer automates this in
// phase 5; until then it's run from an elevated terminal).
static class ServiceInstaller
{
    public const string ServiceName = "VpncBar";

    public static int Install()
    {
        if (!IsAdmin()) return Fail("--install-service must run from an elevated (Administrator) terminal.");
        var exe = Environment.ProcessPath!;
        var bin = $"binPath= \"\\\"{exe}\\\" --service\"";
        // Demand-start: the tray starts/stops the service; it is NOT running at
        // boot. Idempotent — reconfigure if it already exists.
        bool exists = Sc($"query {ServiceName}", quiet: true) == 0;
        var verb = exists
            ? $"config {ServiceName} {bin} start= demand obj= LocalSystem"
            : $"create {ServiceName} {bin} start= demand obj= LocalSystem DisplayName= \"VpncBar Service\"";
        if (Sc(verb) != 0) return 1;
        Sc($"description {ServiceName} \"VpncBar tunnel engine: runs VPN backends, routes and split DNS.\"");

        // Grant Authenticated Users the right to start/stop/query the service,
        // so the non-elevated tray can manage its lifetime. SDDL: SYSTEM and
        // Builtin Admins keep full control; AU gets start(RP)/stop(WP)/query.
        const string sddl =
            "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPLORC;;;AU)";
        if (Sc($"sdset {ServiceName} {sddl}") != 0) return 1;

        Console.Error.WriteLine("VpncBar service installed (demand-start, tray-controlled).");
        return 0;
    }

    public static int Uninstall()
    {
        if (!IsAdmin()) return Fail("--uninstall-service must run from an elevated (Administrator) terminal.");
        Sc($"stop {ServiceName}");      // best effort; service tears tunnels down in OnStop
        if (Sc($"delete {ServiceName}") != 0) return 1;
        Console.Error.WriteLine("VpncBar service removed.");
        return 0;
    }

    static int Sc(string args, bool quiet = false)
    {
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !quiet)
            Console.Error.WriteLine($"sc {args.Split(' ')[0]} failed ({p.ExitCode}):\n{output.Trim()}");
        return p.ExitCode;
    }

    static bool IsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }
}
