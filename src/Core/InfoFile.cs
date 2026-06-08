namespace VpncBar.Core;

// Runtime values --script mode records to <uuid>.info on connect (removed on
// disconnect). Only the gateway/script know these at connect time, so the
// file is the source of truth — port of the mac readTunnelInfo()/TunnelInfo.
sealed class TunnelNetInfo
{
    public string? Iface;
    public string? InternalIP;
    public string? Dns;
    public string? Gateway;
    public string? DefDomain;
    public string? SplitDns;
    public string? MatchDomains;
    public List<string> Routes = [];

    public static TunnelNetInfo Read(Profile p)
    {
        var t = new TunnelNetInfo();
        string raw;
        try { raw = File.ReadAllText(Paths.InfoFile(p)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return t; }
        foreach (var line in raw.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq];
            var value = Profile.Ne(line[(eq + 1)..]);
            switch (key)
            {
                case "TUNDEV": t.Iface = value; break;
                case "INTERNAL_IP4_ADDRESS": t.InternalIP = value; break;
                case "INTERNAL_IP4_DNS": t.Dns = value; break;
                case "VPNGATEWAY": t.Gateway = value; break;
                case "CISCO_DEF_DOMAIN": t.DefDomain = value; break;
                case "CISCO_SPLIT_DNS": t.SplitDns = value; break;
                case "VPNC_MATCH_DOMAINS": t.MatchDomains = value; break;
                case "ROUTE": if (value != null) t.Routes.Add(value); break;
            }
        }
        return t;
    }
}
