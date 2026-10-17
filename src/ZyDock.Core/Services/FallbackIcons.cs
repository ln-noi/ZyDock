using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace ZyDock.Core.Services;

/// <summary>
/// Vector fallbacks used when the shell cannot produce a real icon (missing app,
/// unresolvable shell namespace, custom path that no longer exists). Every icon is a
/// small set of geometries rendered once into a frozen <see cref="DrawingImage"/>,
/// so nothing is allocated while the dock animates.
/// </summary>
public static class FallbackIcons
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);

    public static ImageSource Generic => Get("generic");
    public static ImageSource Folder => Get("folder");
    public static ImageSource Terminal => Get("terminal");
    public static ImageSource Settings => Get("settings");
    public static ImageSource Music => Get("music");
    public static ImageSource Recycle => Get("recycle");
    public static ImageSource Browser => Get("browser");
    public static ImageSource Code => Get("code");
    public static ImageSource Mail => Get("mail");

    public static ImageSource Get(string key)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var image))
            {
                image = Build(key);
                Cache[key] = image;
            }

            return image;
        }
    }

    public static ImageSource ForName(string? name, bool isFolder = false)
    {
        if (isFolder)
        {
            return Folder;
        }

        var n = (name ?? string.Empty).ToLowerInvariant();
        if (Has(n, "终端", "terminal", "cmd", "powershell", "pwsh", "bash", "wt"))
        {
            return Terminal;
        }

        if (Has(n, "设置", "settings", "控制面板", "control"))
        {
            return Settings;
        }

        if (Has(n, "音乐", "music", "spotify", "groove", "media", "播放"))
        {
            return Music;
        }

        if (Has(n, "回收站", "recycle", "trash"))
        {
            return Recycle;
        }

        if (Has(n, "浏览", "edge", "chrome", "firefox", "browser", "internet"))
        {
            return Browser;
        }

        if (Has(n, "code", "studio", "visual", "开发", "rider", "devenv"))
        {
            return Code;
        }

        if (Has(n, "邮件", "mail", "outlook"))
        {
            return Mail;
        }

        return Generic;

        static bool Has(string source, params string[] needles) =>
            needles.Any(needle => source.Contains(needle, StringComparison.Ordinal));
    }

    private static ImageSource Build(string key)
    {
        var spec = key switch
        {
            "folder" => new Spec("#5B8DEF", "#2F5ED0", [FolderBody], []),
            "terminal" => new Spec("#414B59", "#242B34", [], ["M33,40 L46,50 L33,60 M53,60 H69"]),
            "settings" => new Spec("#6E7C90", "#3F4956", Sliders(), []),
            "music" => new Spec("#E06A8B", "#B03F63", [
                Circle(40, 68, 8), Circle(64, 64, 8), "M46,26 H54 V68 H46 Z", "M70,22 H78 V64 H70 Z", "M46,26 H78 V38 L46,43 Z",
            ], []),
            "recycle" => new Spec("#5E6E80", "#333D48", [
                "M43,22 H57 A3,3 0 0 1 57,28 H43 A3,3 0 0 1 43,22 Z",
                "M26,30 H74 A4,4 0 0 1 74,38 H26 A4,4 0 0 1 26,30 Z",
                "M33,42 H67 L63.5,73 A5,5 0 0 1 58.5,77 H41.5 A5,5 0 0 1 36.5,73 Z",
            ], ["M45,50 V68 M55,50 V68"]),
            "browser" => new Spec("#59A9E8", "#2A6FA8", [], [
                CircleStroke(50, 50, 26), "M31,50 A19,26 0 0 1 69,50 A19,26 0 0 1 31,50 Z", "M24,50 H76",
            ]),
            "code" => new Spec("#5C87B8", "#2C4E75", [], ["M40,36 L62,50 L40,64"]),
            "mail" => new Spec("#7B8CDE", "#4756A8", [
                "M26,34 H74 A6,6 0 0 1 80,40 V62 A6,6 0 0 1 74,68 H26 A6,6 0 0 1 20,62 V40 A6,6 0 0 1 26,34 Z",
                "M22,38 L50,56 L78,38",
            ], []),
            _ => new Spec("#59636F", "#2C333B", [], [AppOutline, AppTitleLine]),
        };

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(CreateBackplate(spec.Top, spec.Bottom), null, Backplate));
        group.Children.Add(new GeometryDrawing(Sheen, null, Backplate));

        var glyphBrush = new SolidColorBrush(Color.FromArgb(246, 255, 255, 255));
        glyphBrush.Freeze();
        foreach (var path in spec.Fills)
        {
            group.Children.Add(new GeometryDrawing(glyphBrush, null, Parse(path)));
        }

        foreach (var path in spec.Strokes)
        {
            var pen = new Pen(glyphBrush, 6)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            group.Children.Add(new GeometryDrawing(null, pen, Parse(path)));
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static readonly RectangleGeometry Backplate = CreateBackplate();

    private static readonly LinearGradientBrush Sheen = CreateSheen();

    private const string AppOutline =
        "M30,26 H70 A7,7 0 0 1 77,33 V67 A7,7 0 0 1 70,74 H30 A7,7 0 0 1 23,67 V33 A7,7 0 0 1 30,26 Z";

    private const string AppTitleLine = "M23,40 H77";

    private const string FolderBody =
        "M24,36 H43 A5,5 0 0 1 47,38 L52,45 H76 A5,5 0 0 1 81,50 V68 A6,6 0 0 1 75,74 H25 A6,6 0 0 1 19,68 V42 A6,6 0 0 1 24,36 Z";

    /// <summary>Three slider tracks plus knobs; knobs sit in the gaps so nothing overlaps.</summary>
    private static string[] Sliders() =>
    [
        "M28,32 H72 V38 H28 Z",
        "M28,47 H72 V53 H28 Z",
        "M28,62 H72 V68 H28 Z",
        Circle(40, 35, 8),
        Circle(62, 50, 8),
        Circle(48, 65, 8),
    ];

    private static Geometry Parse(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static RectangleGeometry CreateBackplate()
    {
        var geometry = new RectangleGeometry(new Rect(0, 0, 100, 100), 23, 23);
        geometry.Freeze();
        return geometry;
    }

    private static LinearGradientBrush CreateBackplate(string top, string bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop(ParseColor(top), 0));
        brush.GradientStops.Add(new GradientStop(ParseColor(bottom), 1));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateSheen()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(58, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6));
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static string Circle(double cx, double cy, double r) =>
        string.Create(CultureInfo.InvariantCulture,
            $"M{cx - r},{cy} A{r},{r} 0 1 1 {cx + r},{cy} A{r},{r} 0 1 1 {cx - r},{cy} Z");

    private static string CircleStroke(double cx, double cy, double r) =>
        string.Create(CultureInfo.InvariantCulture,
            $"M{cx - r},{cy} A{r},{r} 0 1 1 {cx + r},{cy} A{r},{r} 0 1 1 {cx - r},{cy} Z");

    private readonly record struct Spec(string Top, string Bottom, string[] Fills, string[] Strokes);
}
