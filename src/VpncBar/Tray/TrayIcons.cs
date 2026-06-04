using Microsoft.Win32;

namespace VpncBar.Tray;

// Lock / open-lock tray icons rendered from Segoe MDL2 Assets glyphs (in-box
// on Win10+), colored for the current taskbar theme — the Windows analog of
// the mac template images lock.fill / lock.open. Re-rendered when the system
// theme changes (TrayContext listens for the change and re-reads these).
static class TrayIcons
{
    public static Icon Locked { get; private set; } = Render((char)0xE72E);     // Lock glyph
    public static Icon Unlocked { get; private set; } = Render((char)0xE785);   // Unlock glyph

    public static void Refresh()
    {
        var oldLocked = Locked;
        var oldUnlocked = Unlocked;
        Locked = Render((char)0xE72E);
        Unlocked = Render((char)0xE785);
        oldLocked.Dispose();
        oldUnlocked.Dispose();
    }

    static Icon Render(char glyph)
    {
        int size = SystemInformation.SmallIconSize.Width;      // DPI-aware (16 at 100%)
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe MDL2 Assets", size * 0.85f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(TaskbarIsLight() ? Color.Black : Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }   // dark taskbar is the common default
    }
}
