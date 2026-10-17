using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ZyDock.App.Rendering;

/// <summary>Everything needed to paint one dock backplate.</summary>
internal sealed record PlateSpec(
    double PlateWidth,
    double PlateHeight,
    double Radius,
    double Scale,
    double Opacity,
    bool Highlights,
    double BorderThickness);

/// <summary>
/// Renders the dock backplate as **clear glass**: a nearly transparent capsule defined by a
/// hairline rim, a soft outer shadow and a few extremely low alpha gradients. No blur, no
/// wallpaper sampling, no grain - the desktop behind simply shows through.
///
/// The bitmap is rendered once per geometry/appearance change, so the per frame cost of the
/// dock is one cached blit plus a handful of icon transforms.
/// </summary>
internal static class GlassRenderer
{
    /// <summary>Room reserved around the plate for the shadow.</summary>
    public const double ShadowMargin = 20;

    public static BitmapSource Render(PlateSpec spec)
    {
        var scale = Math.Max(1.0, spec.Scale);
        var margin = ShadowMargin;
        var width = Math.Max(8, (int)Math.Ceiling((spec.PlateWidth + margin * 2) * scale));
        var height = Math.Max(8, (int)Math.Ceiling((spec.PlateHeight + margin * 2) * scale));

        var plateWidth = spec.PlateWidth * scale;
        var plateHeight = spec.PlateHeight * scale;
        var radius = spec.Radius * scale;

        var root = new Canvas { Width = width, Height = height };

        // Soft floor shadow: this plus the rim is what defines the capsule on bright desktops.
        AddShadow(root, plateWidth, plateHeight, radius, margin * scale, 22 * scale, 80, 6 * scale);
        AddShadow(root, plateWidth, plateHeight, radius, margin * scale, 7 * scale, 54, 2 * scale);

        var plate = new Grid
        {
            Width = plateWidth,
            Height = plateHeight,
            Clip = new RectangleGeometry(new Rect(0, 0, plateWidth, plateHeight), radius, radius),
        };

        // Extremely light graphite wash so the capsule reads as glass instead of nothing.
        var alpha = ToByte(Math.Clamp(spec.Opacity, 0.0, 0.92) * 0.62);
        var bottomAlpha = ToByte(Math.Clamp(spec.Opacity * 1.05 + 0.02, 0.0, 0.94) * 0.62);
        var tint = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        tint.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0x1A, 0x20, 0x2A), 0));
        tint.GradientStops.Add(new GradientStop(Color.FromArgb(bottomAlpha, 0x0B, 0x0E, 0x14), 1));
        plate.Children.Add(new Rectangle { Fill = tint });

        if (spec.Highlights)
        {
            // Top sheen: the brightest part of the design, still only a few percent alpha.
            var sheen = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 255, 255), 0));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            plate.Children.Add(new Rectangle
            {
                Fill = sheen,
                Height = plateHeight * 0.52,
                VerticalAlignment = VerticalAlignment.Top,
            });

            // Hairline inner shade: gives the rim a bevel without looking like a border.
            var innerShade = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            innerShade.GradientStops.Add(new GradientStop(Color.FromArgb(30, 0, 0, 0), 0));
            innerShade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            plate.Children.Add(new Rectangle
            {
                Fill = innerShade,
                Height = Math.Max(3, plateHeight * 0.16),
                VerticalAlignment = VerticalAlignment.Top,
            });

            // Faint light pooling at the bottom edge keeps the capsule floating.
            var floorLight = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1),
                EndPoint = new Point(0.5, 0),
            };
            floorLight.GradientStops.Add(new GradientStop(Color.FromArgb(20, 255, 255, 255), 0));
            floorLight.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            plate.Children.Add(new Rectangle
            {
                Fill = floorLight,
                Height = Math.Max(4, plateHeight * 0.26),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        Canvas.SetLeft(plate, margin * scale);
        Canvas.SetTop(plate, margin * scale);
        root.Children.Add(plate);

        // Rim: one rounded stroke, inside the capsule, thickness chosen by the user (0 = none).
        if (spec.BorderThickness > 0.01)
        {
            var thicknessPx = Math.Max(1.0, spec.BorderThickness * scale);
            var inset = thicknessPx / 2.0;

            var stroke = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            stroke.GradientStops.Add(new GradientStop(Color.FromArgb(104, 255, 255, 255), 0));
            stroke.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 255, 255), 0.55));
            stroke.GradientStops.Add(new GradientStop(Color.FromArgb(48, 255, 255, 255), 1));

            var rim = new Rectangle
            {
                Width = Math.Max(2, plateWidth - thicknessPx),
                Height = Math.Max(2, plateHeight - thicknessPx),
                RadiusX = Math.Max(0, radius - inset),
                RadiusY = Math.Max(0, radius - inset),
                Stroke = stroke,
                StrokeThickness = thicknessPx,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(rim, margin * scale + inset);
            Canvas.SetTop(rim, margin * scale + inset);
            root.Children.Add(rim);

            // A dark companion line right outside keeps the capsule readable on white desktops.
            var outerLine = new Rectangle
            {
                Width = plateWidth + Math.Max(1, scale),
                Height = plateHeight + Math.Max(1, scale),
                RadiusX = radius + 0.5 * scale,
                RadiusY = radius + 0.5 * scale,
                Stroke = new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)),
                StrokeThickness = Math.Max(1, 0.7 * scale),
            };
            Canvas.SetLeft(outerLine, margin * scale - 0.5 * scale);
            Canvas.SetTop(outerLine, margin * scale - 0.5 * scale);
            root.Children.Add(outerLine);
        }

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AddShadow(Canvas root, double plateWidth, double plateHeight, double radius, double left, double blur, byte alpha, double offsetY)
    {
        var shadow = new Border
        {
            Width = plateWidth,
            Height = plateHeight,
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)),
            Effect = new BlurEffect
            {
                Radius = blur,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance,
            },
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(shadow, left);
        Canvas.SetTop(shadow, left + offsetY);
        root.Children.Add(shadow);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
