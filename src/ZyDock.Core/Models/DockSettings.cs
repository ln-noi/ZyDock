namespace ZyDock.Core.Models;

/// <summary>
/// Everything the dock needs to draw itself. Values are persisted as-is and are
/// clamped by <see cref="Clamp"/> so a hand-edited or corrupted file can never
/// produce an invisible or off-screen dock.
/// </summary>
public sealed class DockSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Icon edge length in device independent pixels.</summary>
    public double IconSize { get; set; } = 46;

    /// <summary>Gap between two icons, in device independent pixels.</summary>
    public double IconSpacing { get; set; } = 10;

    /// <summary>Backplate fill opacity. Low values give the near transparent "clear glass" look.</summary>
    public double PanelOpacity { get; set; } = 0.30;

    /// <summary>Distance between the dock and the chosen screen edge.</summary>
    public double EdgeOffset { get; set; } = 12;

    /// <summary>Space between the capsule edge and the first/last icon.</summary>
    public double PaddingHorizontal { get; set; } = 30;

    /// <summary>Space between the capsule edge and the icons, top and bottom.</summary>
    public double PaddingVertical { get; set; } = 18;

    /// <summary>Rim thickness in DIP; 0 = no border at all.</summary>
    public double BorderThickness { get; set; } = 1.0;

    /// <summary>Glass highlights: top sheen, inner bevel and the bottom light pooling.</summary>
    public bool GlassEnabled { get; set; } = true;

    /// <summary>Magnetic magnification on hover.</summary>
    public bool MagnifyEnabled { get; set; } = true;

    /// <summary>Peak magnification factor for the icon under the cursor.</summary>
    public double MagnifyFactor { get; set; } = 1.62;

    /// <summary>Slide the dock away when the mouse leaves it.</summary>
    public bool AutoHideEnabled { get; set; }

    /// <summary>Hide the dock completely while a fullscreen app owns the dock's monitor.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>Ask Windows to auto-hide the taskbar while the dock is running.</summary>
    public bool HideSystemTaskbar { get; set; }

    /// <summary>Show the running-app dot under icons.</summary>
    public bool ShowRunningIndicator { get; set; }

    /// <summary>Show the small name chip above the hovered icon.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Index into the ordered screen list, -1 means "primary screen".</summary>
    public int ScreenIndex { get; set; } = -1;

    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    /// <summary>Start with the current Windows session (HKCU Run entry).</summary>
    public bool AutoStartEnabled { get; set; }

    public List<DockItem> Items { get; set; } = [];

    public DockSettings Clone()
    {
        return new DockSettings
        {
            SchemaVersion = SchemaVersion,
            IconSize = IconSize,
            IconSpacing = IconSpacing,
            PanelOpacity = PanelOpacity,
            EdgeOffset = EdgeOffset,
            GlassEnabled = GlassEnabled,
            BorderThickness = BorderThickness,
            PaddingHorizontal = PaddingHorizontal,
            PaddingVertical = PaddingVertical,
            MagnifyEnabled = MagnifyEnabled,
            MagnifyFactor = MagnifyFactor,
            AutoHideEnabled = AutoHideEnabled,
            HideSystemTaskbar = HideSystemTaskbar,
            HideOnFullscreen = HideOnFullscreen,
            ShowRunningIndicator = ShowRunningIndicator,
            ShowLabels = ShowLabels,
            ScreenIndex = ScreenIndex,
            Edge = Edge,
            AutoStartEnabled = AutoStartEnabled,
            Items = Items.Select(i => i.Clone()).ToList(),
        };
    }

    /// <summary>Forces every value back into a safe, renderable range.</summary>
    public DockSettings Clamp()
    {
        IconSize = Math.Round(Clamp(IconSize, 30, 68), 1);
        IconSpacing = Math.Round(Clamp(IconSpacing, 2, 24), 1);
        PanelOpacity = Math.Round(Clamp(PanelOpacity, 0.05, 0.92), 3);
        EdgeOffset = Math.Round(Clamp(EdgeOffset, 0, 64), 1);
        MagnifyFactor = Math.Round(Clamp(MagnifyFactor, 1.0, 1.9), 2);
        BorderThickness = Math.Round(Clamp(BorderThickness, 0, 3), 2);
        PaddingHorizontal = Math.Round(Clamp(PaddingHorizontal, 0, 80), 0);
        PaddingVertical = Math.Round(Clamp(PaddingVertical, 0, 60), 0);

        if (!Enum.IsDefined(Edge))
        {
            Edge = DockEdge.Bottom;
        }

        Items ??= [];
        Items.RemoveAll(i => i is null);
        foreach (var item in Items)
        {
            item.Target ??= string.Empty;
            item.Name = string.IsNullOrWhiteSpace(item.Name)
                ? DeriveName(item)
                : item.Name.Trim();
        }

        // A dock with zero entries is legal (it can be filled from the settings window)
        // but we refuse absurd counts.
        if (Items.Count > 40)
        {
            Items.RemoveRange(40, Items.Count - 40);
        }

        return this;

        static double Clamp(double value, double min, double max) =>
            double.IsNaN(value) || double.IsInfinity(value) ? min : Math.Min(max, Math.Max(min, value));
    }

    public static string DeriveName(DockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return "未命名";
        }

        try
        {
            if (item.Kind is LaunchKind.Application or LaunchKind.Folder)
            {
                var trimmed = item.Target.TrimEnd('\\', '/');
                var name = Path.GetFileName(trimmed);
                return string.IsNullOrWhiteSpace(name) ? item.Target : name;
            }
        }
        catch (ArgumentException)
        {
        }

        return item.Target;
    }
}
