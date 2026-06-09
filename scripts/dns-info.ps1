# Show the current Windows DNS configuration: per-interface DNS servers + suffix,
# the global suffix search list, the NRPT split-DNS table (what VpncBar writes
# per tunnel), any active VpncBar tunnels, and the resolver cache size.
#
# Read-only. Works in Windows PowerShell 5.1 and PowerShell 7+.
#
#   .\scripts\dns-info.ps1

$ErrorActionPreference = 'SilentlyContinue'

function Section($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }

Section 'Per-interface DNS servers (real servers only)'
# fec0:0:0:ffff::1-3 are Windows' hardcoded default IPv6 DNS placeholders — they
# sit on every adapter, are never actually queried, and only add noise. Strip them
# so what's left is the DNS that genuinely matters.
$placeholders = @('fec0:0:0:ffff::1', 'fec0:0:0:ffff::2', 'fec0:0:0:ffff::3')
$rows = Get-DnsClientServerAddress | Where-Object { $_.ServerAddresses } | ForEach-Object {
    $real = @($_.ServerAddresses | Where-Object { $_ -notin $placeholders })
    if ($real.Count) {
        [PSCustomObject]@{
            InterfaceAlias = $_.InterfaceAlias
            InterfaceIndex = $_.InterfaceIndex
            Family         = if ($_.AddressFamily -eq 23) { 'IPv6' } else { 'IPv4' }
            DnsServers     = $real -join ', '
        }
    }
}
if ($rows) { $rows | Sort-Object InterfaceAlias | Format-Table -Auto } else { '  (none)' }
Write-Host '  Note: VPN tunnel adapters typically carry NO DNS here. Split-DNS is applied' -ForegroundColor DarkGray
Write-Host '  per-domain via the NRPT table below (not global, not per-adapter), so VPN DNS' -ForegroundColor DarkGray
Write-Host '  servers show up there mapped to their domains, not as per-interface entries.' -ForegroundColor DarkGray

Section 'Per-interface DNS suffix + registration'
Get-DnsClient | Where-Object { $_.ConnectionSpecificSuffix -or $_.RegisterThisConnectionsAddress } |
    Select-Object InterfaceAlias, ConnectionSpecificSuffix,
        @{n = 'RegisterDNS'; e = { $_.RegisterThisConnectionsAddress } } |
    Format-Table -Auto

Section 'Global DNS suffix search list'
$search = (Get-DnsClientGlobalSetting).SuffixSearchList
if ($search) { '  ' + ($search -join ', ') } else { '  (none)' }

Section 'NRPT split-DNS table (VpncBar writes one rule per match domain)'
$nrpt = Get-DnsClientNrptRule
if ($nrpt) {
    $nrpt | Select-Object @{n = 'Domain';     e = { $_.Namespace } },
        @{n = 'DnsServers'; e = { $_.NameServers -join ', ' } },
        @{n = 'VpncBar?';   e = { if ($_.Comment -like 'VpncBar:*') { 'yes' } else { '' } } },
        Comment | Format-Table -Auto
} else {
    Write-Host '  (none - no split-DNS rules active; all names use the per-interface servers above)'
}

Section 'Active VpncBar tunnels (%ProgramData%\VpncBar\run\*.info)'
$infos = Get-ChildItem "$env:ProgramData\VpncBar\run\*.info" -ErrorAction SilentlyContinue
if ($infos) {
    foreach ($f in $infos) {
        $h = @{}
        foreach ($line in Get-Content $f.FullName) {
            if ($line -match '^([A-Z0-9_]+)=(.*)$') { $h[$Matches[1]] = $Matches[2] }
        }
        Write-Host ("  $($f.BaseName)") -ForegroundColor Yellow
        "    adapter      = $($h['TUNDEV'])"
        "    internal IP  = $($h['INTERNAL_IP4_ADDRESS'])"
        "    VPN DNS      = $($h['INTERNAL_IP4_DNS'])"
        "    def domain   = $($h['CISCO_DEF_DOMAIN'])"
        "    split DNS    = $($h['CISCO_SPLIT_DNS'])"
        "    match domains= $($h['VPNC_MATCH_DOMAINS'])"
        if (-not ($h['CISCO_DEF_DOMAIN'] -or $h['CISCO_SPLIT_DNS'] -or $h['VPNC_MATCH_DOMAINS'])) {
            Write-Host "    -> no domains: split-DNS skipped; set the profile's DNS match domains to use $($h['INTERNAL_IP4_DNS'])" -ForegroundColor DarkYellow
        }
    }
} else {
    Write-Host '  (no active tunnels)'
}

Section 'DNS resolver cache'
$cache = Get-DnsClientCache
if ($cache) {
    "  $(@($cache).Count) cached records  (Get-DnsClientCache for detail; Clear-DnsClientCache to flush)"
} else {
    '  (empty)'
}
