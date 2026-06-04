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
        // sc.exe quirk: the space after each option= is required.
        if (Sc($"create {ServiceName} binPath= \"\\\"{exe}\\\" --service\" start= auto obj= LocalSystem DisplayName= \"VpncBar Service\"") != 0)
            return 1;
        Sc($"description {ServiceName} \"VpncBar tunnel engine: runs VPN backends, routes and split DNS.\"");
        if (Sc($"start {ServiceName}") != 0) return 1;
        Console.Error.WriteLine("VpncBar service installed and started.");
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

    static int Sc(string args)
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
        if (p.ExitCode != 0) Console.Error.WriteLine($"sc {args.Split(' ')[0]} failed ({p.ExitCode}):\n{output.Trim()}");
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
