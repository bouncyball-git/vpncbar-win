using System.Diagnostics;
using System.Net.NetworkInformation;

namespace VpncBar.Service;

// Network configuration applied per tunnel — the Windows port of the mac
// repo's patched vpnc-script policies:
//   1. NEVER touch the default route; only the gateway's split-include
//      routes are installed (bound to the tunnel adapter), so full-tunnel
//      VPNs coexist instead of fighting over the default gateway.
//   2. Split DNS only, via NRPT: one rule per match domain pointing at the
//      tunnel DNS; if no match domain is known, that tunnel's DNS is
//      SKIPPED, never applied globally.
// Rules are tagged "VpncBar:<uuid>" so disconnect/sweep can find orphans.
// Used by --script mode (connect: runs inside the service's elevated process
// tree) and by the service itself (disconnect cleanup, independent of the
// child's fate — even a hard-killed backend never leaks NRPT rules).
static class NetConfig
{
    public static int? InterfaceIndex(string name)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (string.Equals(nic.Name, name, StringComparison.OrdinalIgnoreCase))
                return nic.GetIPProperties().GetIPv4Properties()?.Index;
        }
        return null;
    }

    // Interface plumbing — what vpnc-script does with ifconfig on unix and
    // vpnc-script-win.js does with netsh upstream: the tunnel IP (netmask
    // defaults to /32 point-to-point) and the negotiated MTU.
    public static void SetAddress(int ifIndex, string addr, string mask, Action<string> log)
    {
        var (code, output) = Run("netsh.exe",
            $"interface ipv4 set address name={ifIndex} source=static address={addr} mask={mask}");
        if (code != 0) log($"set address {addr}/{mask} on if{ifIndex} failed ({code}): {output.Trim()}");
        else log($"address {addr} mask {mask} -> if{ifIndex}");
    }

    public static void SetMtu(int ifIndex, string mtu, Action<string> log)
    {
        var (code, output) = Run("netsh.exe",
            $"interface ipv4 set subinterface {ifIndex} mtu={mtu} store=active");
        if (code != 0) log($"set mtu {mtu} on if{ifIndex} failed ({code}): {output.Trim()}");
        else log($"mtu {mtu} -> if{ifIndex}");
    }

    public static void AddRoute(int ifIndex, string addr, int maskLen, Action<string> log)
    {
        var (code, output) = Run("netsh.exe",
            $"interface ipv4 add route {addr}/{maskLen} interface={ifIndex} store=active");
        // "exists" on a reconnect is fine; anything else is worth surfacing.
        if (code != 0 && !output.Contains("exists", StringComparison.OrdinalIgnoreCase))
            log($"route {addr}/{maskLen} via if{ifIndex} failed ({code}): {output.Trim()}");
        else
            log($"route {addr}/{maskLen} -> if{ifIndex}");
    }

    public static void AddNrptRules(string uuid, IEnumerable<string> domains, IReadOnlyList<string> dns, Action<string> log)
    {
        // One PowerShell invocation for everything: openconnect kills scripts
        // that run longer than 10 seconds, and each powershell.exe start
        // costs ~1s — per-domain invocations would blow the budget.
        var servers = string.Join(",", dns.Select(d => $"'{d}'"));
        var rules = domains.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(d => d.StartsWith('.') ? d : "." + d)
            .ToList();
        var commands = string.Join("; ", rules.Select(ns =>
            $"Add-DnsClientNrptRule -Namespace '{ns}' -NameServers {servers} -Comment 'VpncBar:{uuid}'"))
            + "; Clear-DnsClientCache";
        var (code, output) = Powershell(commands);
        if (code != 0) log($"NRPT add failed: {output.Trim()}");
        else log($"NRPT {string.Join(" ", rules)} -> {string.Join(",", dns)}");
    }

    public static void RemoveNrptRules(string uuid, Action<string> log)
    {
        var (code, output) = Powershell(
            $"Get-DnsClientNrptRule | Where-Object Comment -eq 'VpncBar:{uuid}' | Remove-DnsClientNrptRule -Force");
        if (code != 0) log($"NRPT remove for {uuid} failed: {output.Trim()}");
        Powershell("Clear-DnsClientCache");
    }

    // Remove every VpncBar-tagged NRPT rule (service start sweep: clears
    // leftovers from a crash while no tunnels can be up).
    public static void SweepNrptRules(Action<string> log)
    {
        var (code, output) = Powershell(
            "Get-DnsClientNrptRule | Where-Object { $_.Comment -like 'VpncBar:*' } | Remove-DnsClientNrptRule -Force");
        if (code != 0) log($"NRPT sweep failed: {output.Trim()}");
    }

    static (int Code, string Output) Powershell(string command) =>
        Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"");

    static (int Code, string Output) Run(string file, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") is var ps
                           && file == "powershell.exe" ? ps : Path.Combine(Environment.SystemDirectory, file),
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }
}
