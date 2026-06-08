namespace VpncBar.Core;

// Display formatting shared by the tray menu and the Info tab — ports of the
// mac formatElapsed / humanBytes / grouped helpers.
static class Format
{
    public static string Elapsed(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.Days > 0) return $"{t.Days}d {t.Hours}h";
        if (t.Hours > 0) return $"{t.Hours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string HumanBytes(long n)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = n;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{n} B" : $"{v:F1} {units[i]}";
    }

    public static string Grouped(long n) => n.ToString("N0");
}
