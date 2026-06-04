namespace VpncBar.Core;

// Storage map (docs/PORTING.md §7):
//   profiles (no secrets)  %APPDATA%\vpncbar\profiles.json   (tray-owned)
//   session logs / .info   %ProgramData%\VpncBar\run\        (service-owned)
//   binaries               <install dir>\bin\
static class Paths
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vpncbar");

    public static string ProfilesPath => Path.Combine(ConfigDir, "profiles.json");

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VpncBar");

    public static string RunDir => Path.Combine(ProgramDataDir, "run");

    static string SafeName(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    // "<uuid>_<name>.log" — the per-profile session log the service writes by
    // redirecting the backend child's stdout/stderr (Debug tab tails this).
    public static string LogFile(Profile p) =>
        Path.Combine(RunDir, $"{p.Uuid ?? p.Name}_{SafeName(p.Name)}.log");

    // Per-tunnel runtime info written in --script mode on connect (Info tab).
    public static string InfoFile(Profile p) => Path.Combine(RunDir, $"{p.Uuid ?? p.Name}.info");
}
