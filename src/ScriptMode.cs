using System.Text;
using VpncBar.Core;
using VpncBar.Service;

namespace VpncBar;

// Network-config shim invoked by the backends (openconnect --script / vpnc
// Script) with the standard vpnc-script environment. Runs inside the
// service's elevated process tree, so it applies config directly:
//   connect:    split-include routes (never the default route), NRPT split
//               DNS (skipped when no match domain is known), .info file.
//   disconnect: remove NRPT rules + .info. (The service does this too on
//               kill — this path covers graceful exits; both are idempotent.)
// Stdout goes to the backend's redirected output = the session log.
static class ScriptMode
{
    public static int Run(string[] args)
    {
        var reason = Env("reason");
        var uuid = Env("VPNCBAR_UUID");   // pinned to the profile uuid by the service
                                          // (VPNPID is unusable: openconnect overwrites it with its own pid)
        if (reason == null || uuid == null)
        {
            ServiceEngine.Log($"script: missing reason/VPNCBAR_UUID in environment (reason={reason})");
            return 0;   // never fail the connection over script trouble
        }

        void Log(string msg)
        {
            Console.WriteLine($"[script] {msg}");
            ServiceEngine.Log($"script {reason} {uuid}: {msg}");
        }

        try
        {
            switch (reason)
            {
                case "connect":
                case "reconnect":
                    Configure(uuid, Log);
                    break;
                case "disconnect":
                    NetConfig.RemoveNrptRules(uuid, Log);
                    try { File.Delete(InfoPath(uuid)); } catch (IOException) { }
                    Log("network config removed");
                    break;
                // pre-init / attempt-reconnect: nothing to do
            }
        }
        catch (Exception e)
        {
            Log($"script error: {e.Message}");
        }
        return 0;
    }

    static void Configure(string uuid, Action<string> log)
    {
        var tundev = Env("TUNDEV");
        // openconnect exports the adapter index directly (TUNIDX); fall back
        // to resolving the name for other callers (vpnc in phase 4).
        int? ifIndex = int.TryParse(Env("TUNIDX"), out var idx) ? idx
            : tundev != null ? NetConfig.InterfaceIndex(tundev) : null;
        if (ifIndex == null)
        {
            log($"tunnel interface '{tundev}' not found — no config applied");
        }

        // --- Interface address + MTU (the unix script's ifconfig step) ---
        // vpnc-script always configures the tun interface point-to-point with
        // netmask 255.255.255.255; INTERNAL_IP4_NETMASK is NOT the interface
        // mask — it describes the internal network and becomes a route below.
        if (ifIndex != null && Env("INTERNAL_IP4_ADDRESS") is { } ip4)
        {
            NetConfig.SetAddress(ifIndex.Value, ip4, "255.255.255.255", log);
            if (Env("INTERNAL_IP4_MTU") is { } mtu)
                NetConfig.SetMtu(ifIndex.Value, mtu, log);
        }

        // --- Split-include routes (the default route is never touched) ---
        var routes = new List<string>();
        int includes = int.TryParse(Env("CISCO_SPLIT_INC"), out var n) ? n : 0;
        for (int i = 0; i < includes; i++)
        {
            var addr = Env($"CISCO_SPLIT_INC_{i}_ADDR");
            if (addr == null) continue;
            int maskLen = int.TryParse(Env($"CISCO_SPLIT_INC_{i}_MASKLEN"), out var len)
                ? len
                : MaskToLen(Env($"CISCO_SPLIT_INC_{i}_MASK"));
            if (ifIndex != null) NetConfig.AddRoute(ifIndex.Value, addr, maskLen, log);
            routes.Add($"{addr}/{maskLen}");
        }
        if (includes == 0)
            log("no split-include routes from gateway (default route is never touched — by policy)");

        // The gateway's internal-network route (vpnc-script:264: a network
        // route for INTERNAL_IP4_NETADDR/NETMASK when the gateway sends one).
        if (Env("INTERNAL_IP4_NETMASK") is { } netmask && Env("INTERNAL_IP4_NETADDR") is { } netaddr)
        {
            int len = int.TryParse(Env("INTERNAL_IP4_NETMASKLEN"), out var l) ? l : MaskToLen(netmask);
            if (len < 32)
            {
                if (ifIndex != null) NetConfig.AddRoute(ifIndex.Value, netaddr, len, log);
                routes.Add($"{netaddr}/{len}");
            }
        }

        // --- Scoped DNS via NRPT: gateway domains + the profile's VPN domains ---
        var dns = Split(Env("INTERNAL_IP4_DNS"));
        var domains = Split(Env("CISCO_DEF_DOMAIN"))
            .Concat(Split(Env("CISCO_SPLIT_DNS")))
            .Concat(Split(Env("VPNC_MATCH_DOMAINS")))
            .Where(d => d.Length > 0)
            .ToList();
        if (dns.Count > 0 && domains.Count > 0)
            NetConfig.AddNrptRules(uuid, domains, dns, log);
        else
            log("no match domains known — tunnel DNS skipped, never applied globally (by policy)");

        // --- .info file for the Info tab (same key=value format as macOS) ---
        var info = new StringBuilder();
        info.AppendLine($"TUNDEV={tundev}");
        info.AppendLine($"INTERNAL_IP4_ADDRESS={Env("INTERNAL_IP4_ADDRESS")}");
        info.AppendLine($"INTERNAL_IP4_DNS={Env("INTERNAL_IP4_DNS")}");
        info.AppendLine($"VPNGATEWAY={Env("VPNGATEWAY")}");
        info.AppendLine($"CISCO_DEF_DOMAIN={Env("CISCO_DEF_DOMAIN")}");
        info.AppendLine($"CISCO_SPLIT_DNS={Env("CISCO_SPLIT_DNS")}");
        info.AppendLine($"VPNC_MATCH_DOMAINS={Env("VPNC_MATCH_DOMAINS")}");
        foreach (var r in routes) info.AppendLine($"ROUTE={r}");
        Directory.CreateDirectory(Paths.RunDir);
        File.WriteAllText(InfoPath(uuid), info.ToString());
        log($"configured: if={tundev} routes={routes.Count} dns-domains={domains.Count}");
    }

    static string InfoPath(string uuid) => Path.Combine(Paths.RunDir, $"{uuid}.info");

    static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    static List<string> Split(string? s) =>
        (s ?? "").Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();

    static int MaskToLen(string? mask)
    {
        if (mask == null) return 32;
        try
        {
            var bits = mask.Split('.').Select(byte.Parse)
                .Sum(b => System.Numerics.BitOperations.PopCount(b));
            return bits;
        }
        catch (FormatException) { return 32; }
    }
}
