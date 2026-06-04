using System.Reflection;
using System.Text.RegularExpressions;

namespace VpncBar.Tray;

// Tray + window icons, rendered from assets/vpn-on.svg / vpn-off.svg
// (embedded at build time). A mini SVG renderer covers the logo's vocabulary
// — <rect>, <circle>, <path> with polygonal m/l/z subpaths — drawn in
// document order and scaled by the file's viewBox, so editing the SVGs
// (colors OR geometry) restyles the app on rebuild with no code changes.
// `VpncBar.exe --make-icon <out.ico>` regenerates the exe icon from the
// same art (see tools/make-icon.ps1).
static class TrayIcons
{
    // NB: must be declared before the properties below — static initializers
    // run in textual order, and RenderIcon reaches this cache via Logo().
    static readonly Dictionary<string, SvgLogo> _cache = [];

    public static Icon Connected { get; } = RenderIcon("vpn-on.svg");
    public static Icon Disconnected { get; } = RenderIcon("vpn-off.svg");

    // The exe's embedded icon, reused as the window icon for all forms.
    public static Icon App { get; } =
        Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

    static Icon RenderIcon(string svgName)
    {
        int size = SystemInformation.SmallIconSize.Width;      // DPI-aware (16 at 100%)
        using var bmp = RenderBitmap(svgName, size);
        return Icon.FromHandle(bmp.GetHicon());   // lives for the app's lifetime
    }

    public static Bitmap RenderBitmap(string svgName, int size)
    {
        var logo = Logo(svgName);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float sx = size / logo.VbW, sy = size / logo.VbH;
        foreach (var shape in logo.Shapes)
        {
            using var brush = new SolidBrush(shape.Fill);
            switch (shape)
            {
                case RectShape r:
                    g.FillRectangle(brush, r.X * sx, r.Y * sy, r.W * sx, r.H * sy);
                    break;
                case CircleShape c:
                    g.FillEllipse(brush, (c.Cx - c.R) * sx, (c.Cy - c.R) * sy, 2 * c.R * sx, 2 * c.R * sy);
                    break;
                case PathShape p:
                    foreach (var poly in p.Polys)
                        g.FillPolygon(brush, poly.Select(pt => new PointF(pt.X * sx, pt.Y * sy)).ToArray());
                    break;
            }
        }
        return bmp;
    }

    // Multi-size ICO (PNG-compressed entries) from the off-state art —
    // invoked by `VpncBar.exe --make-icon <path>`.
    public static int WriteIco(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var pngs = sizes.Select(s =>
        {
            using var bmp = RenderBitmap("vpn-off.svg", s);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);   // 0 = 256
            output.Write(dim); output.Write(dim);
            output.Write((byte)0); output.Write((byte)0);        // palette, reserved
            output.Write((ushort)1); output.Write((ushort)32);   // planes, bpp
            output.Write((uint)pngs[i].Length); output.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) output.Write(png);
        Console.Error.WriteLine($"wrote {path} (sizes: {string.Join(", ", sizes)})");
        return 0;
    }

    // ----- mini SVG model + parser -----

    abstract record Shape(Color Fill);
    sealed record RectShape(Color Fill, float X, float Y, float W, float H) : Shape(Fill);
    sealed record CircleShape(Color Fill, float Cx, float Cy, float R) : Shape(Fill);
    sealed record PathShape(Color Fill, PointF[][] Polys) : Shape(Fill);
    sealed record SvgLogo(float VbW, float VbH, List<Shape> Shapes);

    static SvgLogo Logo(string svgName)
    {
        if (_cache.TryGetValue(svgName, out var cached)) return cached;
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().First(n => n.EndsWith(svgName));
        using var stream = asm.GetManifestResourceStream(resource)!;
        var svg = new StreamReader(stream).ReadToEnd();
        return _cache[svgName] = Parse(svg);
    }

    static SvgLogo Parse(string svg)
    {
        var vb = Regex.Match(svg, @"viewBox=""([\d.\-]+)[ ,]+([\d.\-]+)[ ,]+([\d.\-]+)[ ,]+([\d.\-]+)""");
        float vbW = vb.Success ? F(vb.Groups[3].Value) : 1024;
        float vbH = vb.Success ? F(vb.Groups[4].Value) : 1024;

        var shapes = new List<Shape>();
        foreach (Match m in Regex.Matches(svg, @"<(rect|circle|path)\b[^>]*>", RegexOptions.Singleline))
        {
            var tag = m.Value;
            var fill = FillOf(tag);
            if (fill == null) continue;
            switch (m.Groups[1].Value)
            {
                case "rect":
                    shapes.Add(new RectShape(fill.Value, Attr(tag, "x"), Attr(tag, "y"),
                                             Attr(tag, "width"), Attr(tag, "height")));
                    break;
                case "circle":
                    shapes.Add(new CircleShape(fill.Value, Attr(tag, "cx"), Attr(tag, "cy"), Attr(tag, "r")));
                    break;
                case "path":
                    var d = Regex.Match(tag, @"\bd=""([^""]*)""");
                    if (d.Success) shapes.Add(new PathShape(fill.Value, ParsePath(d.Groups[1].Value)));
                    break;
            }
        }
        return new SvgLogo(vbW, vbH, shapes);
    }

    // Polygonal path subset: M/m (moveto), implicit/explicit L/l (lineto),
    // Z/z (closepath). Exactly the vocabulary of the logo's chevrons.
    static PointF[][] ParsePath(string d)
    {
        var polys = new List<PointF[]>();
        var current = new List<PointF>();
        PointF pos = default, start = default;
        char cmd = ' ';
        var tokens = Regex.Matches(d, @"[MmLlZz]|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?");
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i].Value;
            if (t.Length == 1 && char.IsLetter(t[0]))
            {
                if (t is "Z" or "z")
                {
                    if (current.Count > 2) polys.Add(current.ToArray());
                    current.Clear();
                    pos = start;
                }
                else cmd = t[0];
                continue;
            }
            float x = F(t), y = F(tokens[++i].Value);
            bool relative = char.IsLower(cmd);
            var pt = relative ? new PointF(pos.X + x, pos.Y + y) : new PointF(x, y);
            bool isMove = cmd is 'm' or 'M';
            pos = pt;
            if (isMove)
            {
                start = pt;
                cmd = relative ? 'l' : 'L';   // subsequent pairs are implicit linetos
            }
            current.Add(pt);
        }
        if (current.Count > 2) polys.Add(current.ToArray());
        return polys.ToArray();
    }

    // fill from style="…fill:#rrggbb…" (wins, like browsers) or fill="#rrggbb".
    static Color? FillOf(string tag)
    {
        var style = Regex.Match(tag, @"style=""[^""]*fill:\s*#([0-9a-fA-F]{6})");
        var attr = Regex.Match(tag, @"fill=""#([0-9a-fA-F]{6})""");
        var hex = style.Success ? style.Groups[1].Value : attr.Success ? attr.Groups[1].Value : null;
        return hex == null ? null : Color.FromArgb(
            Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..], 16));
    }

    static float Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}=""(-?\d+(?:\.\d+)?)""");
        return m.Success ? F(m.Groups[1].Value) : 0;
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
