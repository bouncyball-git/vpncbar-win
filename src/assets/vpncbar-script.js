// Relay shim: openconnect on Windows can only run scripts via
// `cscript.exe "<script>"` (script.c unconditionally wraps the command), so
// this .js hands straight off to `VpncBar.exe --script` next to it. The
// vpnc-script environment (reason, TUNDEV, INTERNAL_IP4_*, CISCO_*,
// VPNCBAR_UUID, VPNC_MATCH_DOMAINS) is inherited through. Always exits 0 —
// network-config trouble must never kill the connection.
var sh = new ActiveXObject("WScript.Shell");
var fso = new ActiveXObject("Scripting.FileSystemObject");
var exe = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "VpncBar.exe");
try { sh.Run('"' + exe + '" --script', 0, true); } catch (e) { }
WScript.Quit(0);
