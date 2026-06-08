namespace VpncBar.Core;

// Locations of the bundled backend binaries. Both backends + their shared DLL
// closure + wintun.dll live in one "engines" folder beside the running exe
// (build output and the installer lay out the same shape), so there is exactly
// one fixed, service-chosen path per binary.
static class Backends
{
    static string ExeDir => Path.GetDirectoryName(Environment.ProcessPath!)!;
    static string EnginesDir => Path.Combine(ExeDir, "engines");

    public static string OpenconnectExe => Path.Combine(EnginesDir, "openconnect.exe");
    public static bool HasOpenconnect => File.Exists(OpenconnectExe);

    public static string VpncExe => Path.Combine(EnginesDir, "vpnc.exe");
    public static bool HasVpnc => File.Exists(VpncExe);

    // openconnect runs scripts via cscript.exe only — this .js relays to
    // "VpncBar.exe --script" (see assets/vpncbar-script.js). It stays at the
    // app root, next to VpncBar.exe, which it locates relative to itself.
    public static string ScriptJs => Path.Combine(ExeDir, "vpncbar-script.js");
}
