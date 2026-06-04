namespace VpncBar.Core;

// Locations of the bundled backend binaries. Everything lives under the
// directory of the running exe (build output copies vendor/openconnect/bin +
// wintun.dll there; the installer lays out the same shape under Program
// Files), so there is exactly one fixed, service-chosen path per binary.
static class Backends
{
    static string ExeDir => Path.GetDirectoryName(Environment.ProcessPath!)!;

    public static string OpenconnectDir => Path.Combine(ExeDir, "openconnect");
    public static string OpenconnectExe => Path.Combine(OpenconnectDir, "openconnect.exe");
    public static bool HasOpenconnect => File.Exists(OpenconnectExe);

    // openconnect runs scripts via cscript.exe only — this .js relays to
    // "VpncBar.exe --script" (see assets/vpncbar-script.js).
    public static string ScriptJs => Path.Combine(ExeDir, "vpncbar-script.js");

    // vpnc.exe arrives in phase 4
    public static string VpncExe => Path.Combine(ExeDir, "vpnc", "vpnc.exe");
    public static bool HasVpnc => File.Exists(VpncExe);
}
