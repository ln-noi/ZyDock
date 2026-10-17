using System.Windows;

namespace ZyDock.App.Rendering;

/// <summary>
/// Pure geometry for the dock. All values are device independent pixels inside the
/// window; the window itself is placed with physical pixels by <see cref="Views.DockWindow"/>.
/// </summary>
internal sealed class DockLayout
{
    /// <summary>Default horizontal padding (the user can change it in the settings window).</summary>
    public const double DefaultPadHorizontal = 30;

    /// <summary>Default vertical padding (the user can change it in the settings window).</summary>
    public const double DefaultPadVertical = 18;

    public const double ChipHeight = 21;

    public double ShadowMargin => GlassRenderer.ShadowMargin;

    public double IconSize { get; init; }

    public double Spacing { get; init; }

    /// <summary>Space between the capsule edge and the first/last icon.</summary>
    public double PaddingX { get; init; }

    /// <summary>Space between the capsule edge and the icons, top and bottom.</summary>
    public double PaddingY { get; init; }

    public int Count { get; init; }

    public double MaxExtra { get; init; }

    public double PlateWidth { get; init; }

    public double PlateHeight { get; init; }

    public double PlateRadius { get; init; }

    public double Headroom { get; init; }

    public double WindowWidth { get; init; }

    public double WindowHeight { get; init; }

    public double PlateLeft { get; init; }

    public double PlateTop { get; init; }

    public double ItemsTop { get; init; }

    public double ChipTop { get; init; }

    public double DotTop { get; init; }

    public double Pitch => IconSize + Spacing;

    public static DockLayout Compute(
        double iconSize,
        double spacing,
        int count,
        double magnifyFactor,
        bool magnify,
        bool labels,
        double paddingX = DefaultPadHorizontal,
        double paddingY = DefaultPadVertical)
    {
        count = Math.Max(1, count);
        paddingX = Math.Max(0, paddingX);
        paddingY = Math.Max(0, paddingY);

        var plateWidth = count * iconSize + (count - 1) * spacing + paddingX * 2;
        var plateHeight = iconSize + paddingY * 2;
        var radius = plateHeight / 2.0;

        var maxExtra = magnify ? iconSize * (magnifyFactor - 1.0) : 0.0;
        var chipHeight = labels ? ChipHeight : 0.0;
        var headroom = Math.Ceiling(maxExtra) + chipHeight + 16.0;

        const double margin = GlassRenderer.ShadowMargin;
        var windowWidth = plateWidth + margin * 2;
        var windowHeight = headroom + plateHeight + margin;

        return new DockLayout
        {
            IconSize = iconSize,
            Spacing = spacing,
            PaddingX = paddingX,
            PaddingY = paddingY,
            Count = count,
            MaxExtra = maxExtra,
            PlateWidth = plateWidth,
            PlateHeight = plateHeight,
            PlateRadius = radius,
            Headroom = headroom,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            PlateLeft = margin,
            PlateTop = headroom,
            ItemsTop = headroom + paddingY,
            ChipTop = headroom + paddingY - maxExtra - chipHeight - 10,
            DotTop = headroom + plateHeight - 6.5,
        };
    }

    public double ItemLeft(int index) => PlateLeft + PaddingX + index * Pitch;

    public double ItemCenterX(int index) => ItemLeft(index) + IconSize / 2.0;

    public Rect ItemRect(int index) => new(ItemLeft(index), ItemsTop, IconSize, IconSize);

    public Rect PlateRect => new(PlateLeft, PlateTop, PlateWidth, PlateHeight);

    /// <summary>Rounded-rectangle containment used for hit testing (with a small tolerance).</summary>
    public bool ContainsPlate(Point point, double tolerance = 1.0)
    {
        var rect = PlateRect;
        rect.Inflate(tolerance, tolerance);
        if (!rect.Contains(point))
        {
            return false;
        }

        // Corner test: only the two capsule ends are rounded.
        var radius = PlateRadius;
        if (point.X < rect.Left + radius && (point.Y < rect.Top + radius || point.Y > rect.Bottom - radius))
        {
            var cx = rect.Left + radius;
            var cy = point.Y < rect.Top + radius ? rect.Top + radius : rect.Bottom - radius;
            return (point.X - cx) * (point.X - cx) + (point.Y - cy) * (point.Y - cy) <= radius * radius;
        }

        if (point.X > rect.Right - radius && (point.Y < rect.Top + radius || point.Y > rect.Bottom - radius))
        {
            var cx = rect.Right - radius;
            var cy = point.Y < rect.Top + radius ? rect.Top + radius : rect.Bottom - radius;
            return (point.X - cx) * (point.X - cx) + (point.Y - cy) * (point.Y - cy) <= radius * radius;
        }

        return true;
    }

    /// <summary>Index of the icon whose slot contains the point, or -1.</summary>
    public int SlotAt(Point point)
    {
        if (point.Y < ItemsTop - MaxExtra - 6 || point.Y > ItemsTop + IconSize + PaddingY)
        {
            return -1;
        }

        var relative = point.X - (PlateLeft + PaddingX);
        if (relative < -Spacing / 2)
        {
            return -1;
        }

        var index = (int)Math.Floor((relative + Spacing / 2) / Pitch);
        return index >= 0 && index < Count ? index : -1;
    }
}
