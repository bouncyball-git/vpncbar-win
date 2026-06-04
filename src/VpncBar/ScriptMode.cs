namespace VpncBar;

// Network-config shim invoked by vpnc/openconnect with the standard vpnc-script
// environment (reason, TUNDEV, VPNGATEWAY, INTERNAL_IP4_*, CISCO_*, VPNPID,
// VPNC_MATCH_DOMAINS). Runs inside the service's elevated process tree and
// applies routes/NRPT/.info directly. Implemented in phase 3.
static class ScriptMode
{
    public static int Run(string[] args)
    {
        Console.Error.WriteLine("--script mode: not implemented yet (phase 3)");
        return 1;
    }
}
