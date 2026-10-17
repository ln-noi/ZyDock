using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace IconGen;

/// <summary>
/// Draws the ZyDock application icon (graphite capsule with dock dots) at every shell
/// size and writes a multi-resolution .ico with PNG payloads.
/// Usage: IconGen &lt;output.ico&gt; [png-preview.png]
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16];

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "App.ico";
        var preview = args.Length > 1 ? args[1] : null;

        var images = new List<(int Size, byte[] Png)>();
        foreach (var size in Sizes)
        {
            images.Add((size, Render(size)));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        WriteIco(output, images);

        var previewPath = preview ?? Path.ChangeExtension(output, ".preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
        File.WriteAllBytes(previewPath, images[0].Png);
        Console.WriteLine($"icon written: {Path.GetFullPath(output)} ({images.Count} sizes)");
        return 0;
    }

    private static byte[] Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, size, size);
            var radius = size * 0.235;

            // Graphite body with a cold top light.
            var body = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            body.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x44, 0x52), 0));
            body.GradientStops.Add(new GradientStop(Color.FromRgb(0x20, 0x27, 0x31), 0.45));
            body.GradientStops.Add(new GradientStop(Color.FromRgb(0x11, 0x15, 0x1B), 1));

            dc.DrawRoundedRectangle(body, null, rect, radius, radius);

            // Hairline rim.
            var rim = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Math.Max(0.7, size * 0.006));
            dc.DrawRoundedRectangle(null, rim, new Rect(size * 0.004, size * 0.004, size * 0.992, size * 0.992), radius, radius);

            // Glass capsule.
            var capsuleWidth = size * 0.68;
            var capsuleHeight = size * 0.30;
            var capsule = new Rect((size - capsuleWidth) / 2, size * 0.52, capsuleWidth, capsuleHeight);
            var capsuleBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            capsuleBrush.GradientStops.Add(new GradientStop(Color.FromArgb(64, 255, 255, 255), 0));
            capsuleBrush.GradientStops.Add(new GradientStop(Color.FromArgb(26, 255, 255, 255), 1));
            var capsulePen = new Pen(new SolidColorBrush(Color.FromArgb(86, 255, 255, 255)), Math.Max(0.7, size * 0.008));
            dc.DrawRoundedRectangle(capsuleBrush, capsulePen, capsule, capsuleHeight / 2, capsuleHeight / 2);

            // Three dock dots.
            var dotRadius = size * 0.043;
            var dotY = capsule.Top + capsule.Height / 2;
            var spacing = capsuleWidth * 0.235;
            var accent = new LinearGradientBrush(
                Color.FromRgb(0x8A, 0xB4, 0xFF),
                Color.FromRgb(0x3F, 0x72, 0xD8),
                new Point(0, 0),
                new Point(0, 1));
            var white = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255));

            for (var i = -1; i <= 1; i++)
            {
                var center = new Point(size / 2.0 + i * spacing, dotY);
                var brush = i == 0 ? (Brush)accent : white;
                dc.DrawEllipse(brush, null, center, dotRadius, dotRadius);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteIco(string path, List<(int Size, byte[] Png)> images)
    {
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)images.Count); // count

        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);   // palette
            writer.Write((byte)0);   // reserved
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }
    }
}
