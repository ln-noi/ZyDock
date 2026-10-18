using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using ZyDock.App.Interop;
using ZyDock.App.Rendering;
using ZyDock.Core.Diagnostics;
using ZyDock.Core.Models;
using ZyDock.Core.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ZyDock.App.Views;

/// <summary>
/// The dock itself: a transparent, non activating, topmost window that hosts the glass
/// backplate and the icon row. All per-frame work happens in one rendering callback that
/// detaches itself the moment everything has settled.
/// </summary>
public partial class DockWindow : Window
{
    private const double MotionTau = 0.052;

    private readonly SettingsService _settingsService;
    private readonly IconProvider _icons;
    private readonly AppLauncher _launcher;
    private readonly RunningAppTracker _running;

    private readonly List<IconCell> _cells = [];

    private DockSettings _settings;
    private DockLayout _layout = DockLayout.Compute(46, 10, 1, 1.62, true, true);
    private ScreenInfo? _screen;
    private double _scale = 1.0;
    private PixelRect _desiredPx;

    private DispatcherTimer? _runningTimer;
    private DispatcherTimer? _probeTimer;
    private DispatcherTimer? _toastTimer;
    private DispatcherTimer? _verifyTimer;

    private HwndSource? _source;
    private bool _renderingHooked;
    private long _lastTick;

    private int _hoverIndex = -1;
    private int _pressedIndex = -1;
    private int _chipIndex = -1;
    private bool _pointerInside;
    private double _pointerX;

    private bool _hidden;
    private double _offsetCurrent;
    private double _offsetTarget;
    private double _chipOpacityCurrent;
    private double _chipOpacityTarget;
    private string? _chipText;
    private bool _nudgePending;

    private bool _disposed;
    private bool _runningBusy;
    private int _frameSamples;
    private double _frameIntervalSum;

    public DockWindow(SettingsService settingsService, IconProvider icons, AppLauncher launcher, RunningAppTracker running)
    {
        _settingsService = settingsService;
        _icons = icons;
        _launcher = launcher;
        _running = running;
        _settings = settingsService.Current;

        InitializeComponent();

        // Accept files dropped from Explorer (adds them to the dock).
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        Loaded += OnLoaded;
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (InputDebug)
        {
            Log.Info("[input] automation hooks enabled (ZYDOCK_DEBUG_INPUT)");
        }
        ApplySettings(_settings, rebuildItems: true);
    }

    /// <summary>Raised when the user right clicks an icon; the host shows the menu.</summary>
    internal event Action<DockItem, Point>? ItemContextRequested;

    /// <summary>Raised when the user right clicks the empty part of the capsule (screen point).</summary>
    internal event Action<Point>? DockContextRequested;

    /// <summary>One or more entries were dropped onto the dock from Explorer.</summary>
    internal event Action<IReadOnlyList<DockItem>>? ItemsDropped;

    /// <summary>Icons were reordered by dragging inside the dock (new display order).</summary>
    internal event Action<IReadOnlyList<DockItem>>? ItemsReordered;

    /// <summary>
    /// Top edge of the capsule in physical screen pixels. Menus anchored on the dock use it as a
    /// hard upper bound so they open above the capsule instead of covering the taskbar.
    /// </summary>
internal int PlateTopPixels
    {
        get
        {
            // Query the live window rect: menus are opened long after the layout settled, and a
            // stale cached rect here would silently disable the "stay above the dock" bound.
            if (_source is not null && NativeMethods.GetWindowRect(_source.Handle, out var rect))
            {
                return rect.Top + (int)Math.Round(_layout.PlateTop * _scale);
            }

            return _desiredPx.Top + (int)Math.Round(_layout.PlateTop * _scale);
        }
    }

    internal DockSettings Settings => _settings;

    internal DockLayout Layout => _layout;

    #region settings / layout

    /// <summary>Applies a settings snapshot. <paramref name="rebuildItems"/> recreates the icons.</summary>
    public void ApplySettings(DockSettings settings, bool rebuildItems)
    {
        _settings = settings;

        if (rebuildItems || _cells.Count != settings.Items.Count)
        {
            RebuildItems();
        }
        else
        {
            for (var i = 0; i < _cells.Count && i < settings.Items.Count; i++)
            {
                _cells[i].Item = settings.Items[i];
            }
        }

        RebuildLayout();
        RefreshRunningStates(force: true);
        ApplyAutoHideMode();
        EvaluateFullscreen();
    }

    /// <summary>Integer device-pixel size the icon bitmaps were pre-rendered at.</summary>
    private int _iconPixelSize;

    /// <summary>
    /// Halves a bitmap with a plain linear filter. Exact 2:1 halving is a box filter, which keeps
    /// more edge contrast than one large downscale pass.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource Halve(System.Windows.Media.Imaging.BitmapSource source)
    {
        var width = Math.Max(1, source.PixelWidth / 2);
        var height = Math.Max(1, source.PixelHeight / 2);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Linear);
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>
    /// Pre-renders an icon at exactly the device-pixel size it occupies on screen, so the resting
    /// dock blits it 1:1 with no resampling at all. The shell hands us a 256 px bitmap and the
    /// dock slot is ~69 px: doing that division once here, with Fant, is what actually looks
    /// crisp. A BitmapCache cannot do this because it is resampled again by the compositor.
    /// </summary>
    private System.Windows.Media.Imaging.BitmapSource RenderIconForPixelSize(ImageSource? source)
    {
        var scale = _scale <= 0 ? 1.0 : _scale;
        var iconDip = _layout?.IconSize is > 1 ? _layout.IconSize : _settings.IconSize;
        var pixels = Math.Max(8, (int)Math.Round(iconDip * scale));
        _iconPixelSize = pixels;

        source ??= FallbackIcons.Generic;

        // Mipmap-style downscale: halve while the bitmap is still much larger than the slot, then
        // finish with one Fant pass. Exact 2:1 halving is a box filter and keeps edges crisper
        // than a single huge downscale.
        if (source is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            while (bitmap.PixelWidth > pixels * 2 && bitmap.PixelWidth > 16)
            {
                bitmap = Halve(bitmap);
            }

            source = bitmap;
        }

        // RenderTargetBitmap maps one drawing unit to `dpi / 96` pixels, so a visual drawn in a
        // `pixels`-unit square would only fill `pixels` of the bitmap's `pixels * scale`
        // device pixels - which is exactly what cropped the icons to two thirds. The drawing
        // rect therefore has to be expressed in the visual's DIP units: pixels / scale.
        var visualUnits = pixels / scale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect(0, 0, visualUnits, visualUnits));
        }

        // The DPI is chosen so the bitmap reports a DIP size equal to the icon slot
        // (pixels / scale == iconDip). That turns the draw into a 1:1 device-pixel blit
        // instead of yet another resample.
        var dpi = 96.0 * scale;
        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            pixels, pixels, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private void RebuildItems()
    {
        ItemLayer.Children.Clear();
        DotLayer.Children.Clear();
        _cells.Clear();

        foreach (var item in _settings.Items)
        {
            var scaleTransform = new ScaleTransform(1, 1);
            var image = new Image
            {
                Source = RenderIconForPixelSize(_icons.GetIcon(item)),
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                IsHitTestVisible = false,
            };

            // High quality (Fant) is back: the icon is rasterised ONCE into the BitmapCache
            // below, and per frame only that small cached bitmap gets scaled. The previous
            // LowQuality switch was what produced the soft / jagged icon edges.
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            // The cache has to live on the Image, not on the host Grid: a BitmapCache rasterises
            // only the element bounds, which would clip the reflection that overflows below.
            // No BitmapCache on purpose. A cache is a fixed-resolution bitmap that has to be
            // resampled to the physical pixel grid every frame, and that resample is exactly
            // what makes a 256 px icon look soft sitting in a 69 px slot. Letting WPF resample
            // the original 256 px bitmap with Fant each frame keeps the edges genuinely sharp;
            // the icons are small so the cost is a handful of cached image draws.

            var host = new Grid
            {
                Width = _settings.IconSize,
                Height = _settings.IconSize,
                RenderTransform = scaleTransform,
                RenderTransformOrigin = new Point(0.5, 1.0),
                IsHitTestVisible = false,
                // No CacheMode here on purpose: a BitmapCache rasterises just the element
                // bounds and would clip the reflection that overflows below the icon.
                // The cache lives on the Image instead.
            };
            host.Children.Add(image);

            // Faint mirrored reflection under the icon ("拖影"), off by default.
            if (_settings.IconReflection)
            {
                var reflection = CreateReflection(
                    image.Source,
                    _settings.IconSize,
                    _settings.ReflectionSize,
                    _settings.ReflectionOpacity);
                if (reflection is not null)
                {
                    host.Children.Add(reflection);
                }
            }

            ItemLayer.Children.Add(host);
            _cells.Add(new IconCell(item, host, image, scaleTransform));
        }

        _chipIndex = -1;
        Chip.Visibility = Visibility.Collapsed;
    }

    private void RebuildLayout()
    {
        _screen = Displays.Resolve(Displays.All(), _settings.ScreenIndex);
        _scale = _screen.Scale;

        // Fit on screen: with a lot of icons the requested icon size would push the capsule
        // past the screen edges, so it shrinks (down to 24 px) until everything fits.
        var iconSize = _settings.IconSize;
        var spacing = _settings.IconSpacing;
        var count = Math.Max(1, _cells.Count);
        var availableWidth = _screen.WorkWidth / _scale - 64;

        for (var guard = 0; guard < 80; guard++)
        {
            var plateWidth = count * iconSize + (count - 1) * spacing + _settings.PaddingHorizontal * 2;
            if (plateWidth <= availableWidth || iconSize <= 24)
            {
                break;
            }

            iconSize = Math.Max(24, iconSize * 0.97);
            spacing = Math.Max(2, spacing * 0.97);
        }

        _layout = DockLayout.Compute(
            iconSize,
            spacing,
            count,
            _settings.MagnifyFactor,
            _settings.MagnifyEnabled,
            _settings.ShowLabels,
            _settings.PaddingHorizontal,
            _settings.PaddingVertical,
            ReflectionHeightFor(iconSize));

        Width = _layout.WindowWidth;
        Height = _layout.WindowHeight;

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            cell.Root.Width = _layout.IconSize;
            cell.Root.Height = _layout.IconSize;
            Canvas.SetLeft(cell.Root, SnapToDevicePixel(_layout.ItemLeft(i)));
            Canvas.SetTop(cell.Root, SnapToDevicePixel(_layout.ItemsTop));
            cell.Scale.CenterX = _layout.IconSize / 2;
            cell.Scale.CenterY = _layout.IconSize;
        }

        PositionWindow();
        RebuildPlate();
        RebuildDots();

        _offsetCurrent = _hidden ? _layout.WindowHeight : 0;
        _offsetTarget = _offsetCurrent;
        DockOffset.Y = _offsetCurrent;
    }

    private void PositionWindow()
    {
        var screen = _screen ?? Displays.Resolve(Displays.All(), _settings.ScreenIndex);
        var scale = screen.Scale;

        var windowPxWidth = (int)Math.Round(_layout.WindowWidth * scale);
        var windowPxHeight = (int)Math.Round(_layout.WindowHeight * scale);

        // Anchor to the *actual* bottom edge: the monitor work area reports a full screen as
        // soon as the taskbar auto-hide bit is set, even when Explorer keeps the bar visible.
        var taskbarInset = TaskbarProbe.GetBottomInsetPx(screen);
        var bottomEdge = screen.Bottom - taskbarInset;

        var plateBottomPx = bottomEdge - (int)Math.Round(_settings.EdgeOffset * scale);
        var windowBottomPx = plateBottomPx + (int)Math.Round(_layout.ShadowMargin * scale);
        var windowTopPx = windowBottomPx - windowPxHeight;

        var windowLeftPx = screen.WorkLeft + (screen.WorkWidth - windowPxWidth) / 2;

        _desiredPx = new PixelRect(windowLeftPx, windowTopPx, windowPxWidth, windowPxHeight);

        Left = windowLeftPx / scale;
        Top = windowTopPx / scale;
        Width = windowPxWidth / scale;
        Height = windowPxHeight / scale;

        _nudgePending = true;
    }

    private void NudgeToDesired()
    {
        if (!_nudgePending || _source is null)
        {
            return;
        }

        _nudgePending = false;

        try
        {
            if (!NativeMethods.GetWindowRect(_source.Handle, out var rect))
            {
                return;
            }

            var dx = _desiredPx.Left - rect.Left;
            var dy = _desiredPx.Top - rect.Top;
            if (dx != 0 || dy != 0)
            {
                Left += dx / _scale;
                Top += dy / _scale;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("修正 Dock 窗口位置失败。", ex);
        }
    }

    /// <summary>Snaps a DIP coordinate to a whole physical pixel so icons never sit half-pixel
    /// off, which is the other common source of soft-looking bitmaps.</summary>
    private double SnapToDevicePixel(double dip)
    {
        var scale = _scale <= 0 ? 1.0 : _scale;
        return Math.Round(dip * scale) / scale;
    }

    /// <summary>Parses "#RRGGBB" into a Color, falling back to the default tint.</summary>
    private static Color ParseTint(string? value)
    {
        try
        {
            var text = DockSettings.NormalizeColor(value);
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(DockSettings.DefaultTint);
        }
    }

    private void RebuildPlate()
    {
        try
        {
            var scale = _scale;

            var spec = new PlateSpec(
                _layout.PlateWidth,
                _layout.PlateHeight,
                _layout.PlateRadius,
                scale,
                _settings.PanelOpacity,
                _settings.GlassEnabled,
                _settings.BorderThickness,
                _settings.ShadowEnabled,
                ParseTint(_settings.TintColor),
                _settings.TintEnabled,
                _settings.SpecularStrength);

            var bitmap = GlassRenderer.Render(spec);
            PlateImage.Source = bitmap;
            // The plate never changes while the icons animate: cache it as one bitmap so the
            // software compositor blits it instead of re-rendering shadows and strokes.
            PlateImage.CacheMode = new BitmapCache();
            PlateImage.Width = bitmap.PixelWidth / scale;
            PlateImage.Height = bitmap.PixelHeight / scale;
            // The bitmap already contains the shadow margin, so it must be placed relative to
            // the plate rect - not at -margin - otherwise the capsule slides left of the icons.
            Canvas.SetLeft(PlateImage, _layout.PlateLeft - GlassRenderer.ShadowMargin);
            Canvas.SetTop(PlateImage, _layout.PlateTop - GlassRenderer.ShadowMargin);
        }
        catch (Exception ex)
        {
            Log.Error("渲染 Dock 底板失败。", ex);
        }
    }

    /// <summary>
    /// Builds a mirrored, fading copy of the icon that sits just below it ("拖影"/reflection).
    /// It lives inside the icon host, so it follows the magnification transform for free.
    /// </summary>
    /// <summary>
    /// Height the reflection will occupy under one icon. The layout uses the same formula as the
    /// renderer so the capsule is sized around "icon + reflection" as a single block.
    /// </summary>
    private double ReflectionHeightFor(double iconSize)
    {
        if (!_settings.IconReflection)
        {
            return 0;
        }

        var height = iconSize * 0.30 * Math.Clamp(_settings.ReflectionSize, 0, 1.6);
        return height < 2 ? 0 : height;
    }

    private static UIElement? CreateReflection(
        ImageSource? source,
        double iconSize,
        double sizeMultiplier,
        double opacity)
    {
        // Base height is 30% of the icon; the "拖影大小" slider scales it (0 = off, 1.6 = large).
        var height = iconSize * 0.30 * Math.Clamp(sizeMultiplier, 0, 1.6);
        if (height < 2)
        {
            return null;
        }

        // A Canvas measures its children with infinite space, so the reflection can hang below
        // the icon cell without being squeezed to zero height (a Grid would do exactly that).
        var layer = new Canvas
        {
            Width = iconSize,
            Height = iconSize,
            IsHitTestVisible = false,
        };

        var reflection = new Image
        {
            Source = source,

            // Uniform keeps the icon's own aspect ratio: the reflection is a scaled-down copy of
            // the icon, never a squashed one. The box is as wide as the icon cell and as tall as
            // the chosen reflection height, so Uniform renders it at height x height, centred.
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Width = iconSize,
            Height = height,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, -1),
            Opacity = Math.Clamp(opacity, 0, 0.6),

            // Progressive fade: the element is flipped, so its "end" is the edge that touches the
            // icon. Keep that edge strongest and let the far edge dissolve completely.
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.45),
                    new GradientStop(Color.FromArgb(225, 255, 255, 255), 0.85),
                    new GradientStop(Color.FromArgb(245, 255, 255, 255), 1),
                },
            },
        };

        RenderOptions.SetBitmapScalingMode(reflection, BitmapScalingMode.HighQuality);

        Canvas.SetLeft(reflection, 0);
        Canvas.SetTop(reflection, iconSize + DockLayout.ReflectionGap);
        layer.Children.Add(reflection);

        return layer;
    }

    private void RebuildDots()
    {
        DotLayer.Children.Clear();

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var available = _launcher.IsReachable(cell.Item);
            cell.Available = available;
            cell.Image.Opacity = available ? 1.0 : 0.45;

            var dot = new Ellipse
            {
                Width = 3.5,
                Height = 3.5,
                Fill = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(dot, _layout.ItemCenterX(i) - 1.75);
            Canvas.SetTop(dot, _layout.DotTop);
            DotLayer.Children.Add(dot);
            cell.Dot = dot;
        }
    }

    #endregion

    #region window plumbing

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(NudgeToDesired));
        StartRunningTimer();
        StartProbeTimer();
        StartFullscreenWatch();

        var refresh = Displays.GetRefreshRateHz();
        Log.Info(refresh > 0
            ? $"显示器刷新率 {refresh} Hz：动画按屏幕 vsync 逐帧驱动（每帧约 {1000.0 / refresh:0.00} ms）。"
            : "未能读取刷新率，动画仍按屏幕 vsync 逐帧驱动。");
    }

    #region fullscreen

    private static readonly bool WatchDebug = Environment.GetEnvironmentVariable("ZYDOCK_WATCH_DEBUG") == "1";

    private System.Threading.Timer? _fullscreenTimer;
    private bool _fullscreenHidden;

    private void StartFullscreenWatch()
    {
        if (_fullscreenTimer is not null)
        {
            return;
        }

        // A DispatcherTimer stops being serviced once the dock window is hidden - and hiding
        // the window is exactly what this watcher does, so the dock stayed hidden forever.
        // A thread-pool timer keeps ticking regardless of window visibility and marshals the
        // actual check back to the UI thread.
        _fullscreenTimer = new System.Threading.Timer(
            _ => PostEvaluateFullscreen(),
            null,
            dueTime: 500,
            period: 400);
    }

    /// <summary>
    /// Hides the dock while the foreground window is maximized or fullscreen on this monitor
    /// (games, video players, browser fullscreen, any maximized window) and brings it back afterwards.
    /// </summary>
    private void PostEvaluateFullscreen()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(new Action(EvaluateFullscreen), DispatcherPriority.Background);
        }
        catch (Exception)
        {
            // Dispatcher already shutting down.
        }
    }

    private void EvaluateFullscreen()
    {
        if (_disposed || _settings is null)
        {
            return;
        }

        var handle = _source?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!_settings.HideOnFullscreen)
        {
            if (_fullscreenHidden)
            {
                LeaveFullscreen();
            }

            return;
        }

        var fullscreen = WindowCoverageWatcher.CoversDesktop(Displays.MonitorOf(handle), handle);

        if (WatchDebug)
        {
            Log.Info($"[watch] fullscreen={fullscreen} hidden={_fullscreenHidden} visible={IsVisible}");
        }

        if (fullscreen && !_fullscreenHidden)
        {
            EnterFullscreen();
        }
        else if (!fullscreen && _fullscreenHidden)
        {
            LeaveFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        _fullscreenHidden = true;
        Log.Info("前台窗口已最大化/全屏，Dock 已自动隐藏（还原窗口后恢复）。");

        try
        {
            ResetMagnification();
            HideChip();
            Hide();
        }
        catch (Exception ex)
        {
            Log.Warn("隐藏 Dock 失败。", ex);
        }
    }

    private void LeaveFullscreen()
    {
        _fullscreenHidden = false;
        Log.Info("前台窗口已还原，Dock 恢复显示。");

        try
        {
            Show();
            _nudgePending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(NudgeToDesired));
            SetHidden(AutoHideEnabled && !_pointerInside);
        }
        catch (Exception ex)
        {
            Log.Warn("恢复 Dock 显示失败。", ex);
        }
    }

    #endregion

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = PresentationSource.FromVisual(this) as HwndSource;
        if (_source is null)
        {
            return;
        }

        _source.AddHook(WndProc);

        var exStyle = NativeMethods.GetWindowLongPtr(_source.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_source.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (InputDebug && msg >= 0x0400)
        {
            Log.Info($"[input] msg=0x{msg:X} w={wParam}");
        }

        if (msg == NativeMethods.WM_NCHITTEST)
        {
            handled = true;
            var hit = HitTest(lParam);
            if (InputDebug && Environment.GetEnvironmentVariable("ZYDOCK_DEBUG_INPUT") == "2")
            {
                Log.Info($"[input] NCHITTEST hit={hit} point={ScreenToDip(lParam)}");
            }

            return new IntPtr(hit ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
        }

        if (InputDebug && msg != 0)
        {
            // Automation hooks: drive the production hover / click code paths from a test
            // script without synthesised input. Only registered when ZYDOCK_DEBUG_INPUT=1.
            if (_hoverMessage == 0)
            {
                _hoverMessage = RegisterWindowMessageW("ZyDock.DebugHover");
                _clickMessage = RegisterWindowMessageW("ZyDock.DebugClick");
                _menuMessage = RegisterWindowMessageW("ZyDock.DebugMenu");
                _dropMessage = RegisterWindowMessageW("ZyDock.DebugDrop");
                _reorderMessage = RegisterWindowMessageW("ZyDock.DebugReorder");
            }

            if (msg == _dropMessage)
            {
                handled = true;

                // Simulates the Explorer drop path with one real file and one real folder.
                var candidates = new[]
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                };

                var dropped = new List<DockItem>();
                foreach (var path in candidates)
                {
                    var item = DockItemFactory.FromPath(path);
                    if (item is not null &&
                        !_settings.Items.Any(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
                    {
                        dropped.Add(item);
                    }
                }

                Log.Info($"[input] 调试拖入 {dropped.Count} 项：" + string.Join(", ", dropped.Select(d => d.Name)));
                if (dropped.Count > 0)
                {
                    ItemsDropped?.Invoke(dropped);
                }

                return IntPtr.Zero;
            }

            if (msg == _reorderMessage)
            {
                handled = true;
                var from = wParam.ToInt32();
                var to = lParam.ToInt32();
                if (from >= 0 && from < _cells.Count && to >= 0 && to < _cells.Count && from != to)
                {
                    var order = Enumerable.Range(0, _cells.Count).ToList();
                    order.Remove(from);
                    order.Insert(Math.Clamp(to, 0, order.Count), from);
                    var newOrder = order.Select(i => _cells[i].Item).ToList();
                    Log.Info($"[input] 调试排序 {from}->{to}：" + string.Join(" > ", newOrder.Select(i => i.Name)));
                    ItemsReordered?.Invoke(newOrder);
                }

                return IntPtr.Zero;
            }

            if (msg == _menuMessage)
            {
                handled = true;
                var point = new Point(wParam.ToInt32(), lParam.ToInt32());
                var index = HitItem(point);
                Log.Info($"[input] 调试右键 {point.X},{point.Y} -> 索引 {index}");
                if (index >= 0)
                {
                    ItemContextRequested?.Invoke(_cells[index].Item, point);
                }
                else
                {
                    DockContextRequested?.Invoke(PointToScreen(point));
                }

                return IntPtr.Zero;
            }

            if (msg == _hoverMessage)
            {
                handled = true;
                var x = wParam.ToInt32();
                UpdateTargets(x);
                UpdateHover(new Point(x, _layout.ItemsTop + 8));
                return IntPtr.Zero;
            }

            if (msg == _clickMessage)
            {
                handled = true;
                var x = wParam.ToInt32();
                var y = lParam.ToInt32();
                var index = HitItem(new Point(x, y));
                Log.Info($"[input] 调试点击 {x},{y} -> 索引 {index}");
                if (index >= 0)
                {
                    Launch(_cells[index].Item);
                }

                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private int _hoverMessage;
    private int _clickMessage;
    private int _menuMessage;
    private int _dropMessage;
    private int _reorderMessage;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string lpString);

    /// <summary>
    /// Automation hooks (simulated hover / click / drop / reorder) exist only in Debug builds
    /// and in builds that explicitly define ZYDOCK_AUTOMATION (used to verify a release build).
    /// A shipped Release binary has no code path reacting to those window messages, so another
    /// process cannot drive the dock.
    /// </summary>
    private static bool InputDebug
    {
        get
        {
#if DEBUG || ZYDOCK_AUTOMATION
            return Environment.GetEnvironmentVariable("ZYDOCK_DEBUG_INPUT") == "1";
#else
            return false;
#endif
        }
    }

    private Point ScreenToDip(IntPtr lParam)
    {
        try
        {
            if (_source is null || !NativeMethods.GetWindowRect(_source.Handle, out var rect))
            {
                return default;
            }

            var raw = lParam.ToInt64();
            var x = (short)(raw & 0xFFFF);
            var y = (short)((raw >> 16) & 0xFFFF);
            return new Point((x - rect.Left) / _scale, (y - rect.Top) / _scale);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Only the plate, the icons and the chip may swallow mouse input. Everything else
    /// (window headroom, the shadow halo, the transparent gaps) is reported as
    /// HTTRANSPARENT so clicks reach the desktop and other windows.
    /// </summary>
    private bool HitTest(IntPtr lParam)
    {
        if (_hidden || _source is null)
        {
            return false;
        }

        try
        {
            var raw = lParam.ToInt64();
            var screenX = (short)(raw & 0xFFFF);
            var screenY = (short)((raw >> 16) & 0xFFFF);

            if (!NativeMethods.GetWindowRect(_source.Handle, out var rect))
            {
                return true;
            }

            var point = new Point((screenX - rect.Left) / _scale, (screenY - rect.Top) / _scale);

            if (_layout.ContainsPlate(point))
            {
                return true;
            }

            if (_chipIndex >= 0 && Chip.Visibility == Visibility.Visible)
            {
                var chipRect = new Rect(Canvas.GetLeft(Chip), Canvas.GetTop(Chip), Chip.ActualWidth, Chip.ActualHeight);
                chipRect.Inflate(2, 2);
                if (chipRect.Contains(point))
                {
                    return true;
                }
            }

            if (Toast.Visibility == Visibility.Visible)
            {
                var toastRect = new Rect(Canvas.GetLeft(Toast), Canvas.GetTop(Toast), Toast.ActualWidth, Toast.ActualHeight);
                toastRect.Inflate(2, 2);
                if (toastRect.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Re-resolves the target screen and re-places the window (used after the taskbar state or
    /// the work area changed, without rebuilding the icon tree).
    /// </summary>
    internal void RefreshPlacement()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RebuildLayout();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(NudgeToDesired));
            }
            catch (Exception ex)
            {
                Log.Warn("重新定位窗口失败。", ex);
            }
        }));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RebuildLayout();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(NudgeToDesired));
            }
            catch (Exception ex)
            {
                Log.Warn("响应显示设置变化失败。", ex);
            }
        }));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RebuildLayout();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(NudgeToDesired));
        }));
    }

    #endregion

    #region input

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(RootCanvas);
        if (InputDebug)
        {
            Log.Info($"[input] MouseMove {point.X:0.0},{point.Y:0.0} hidden={_hidden}");
        }

        _pointerInside = true;
        _pointerX = point.X;

        // While a fullscreen app owns this monitor the dock is hidden and must not react.
        if (_fullscreenHidden)
        {
            return;
        }

        if (_hidden)
        {
            SetHidden(false);
            return;
        }

        if (AutoHideEnabled && _offsetTarget > 0)
        {
            SetHidden(false);
        }

        // Dragging an icon to reorder: no magnifier, no tooltip, only the preview.
        if (_pressIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_reorderActive &&
                (Math.Abs(point.X - _pressPoint.X) > DragThreshold || Math.Abs(point.Y - _pressPoint.Y) > DragThreshold))
            {
                BeginReorder();
            }

            if (_reorderActive)
            {
                UpdateReorderPreview(point);
                return;
            }
        }

        UpdateTargets(point.X);
        UpdateHover(point);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointerInside = false;

        if (_reorderActive)
        {
            // Keep the drag alive even if the pointer slips out of the window.
            return;
        }

        ResetMagnification();
        HideChip();

        if (AutoHideEnabled)
        {
            SetHidden(true);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(RootCanvas);
        var index = HitItem(point);
        _pressedIndex = index;

        if (index >= 0)
        {
            _pressIndex = index;
            _pressPoint = point;
            UpdateTargets(point.X);
            e.Handled = true;

            // Capture immediately: the implicit capture a non-activating layered window gets is
            // unreliable, and without it the drag never sees the intermediate moves.
            CaptureMouseQuietly();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_reorderActive)
        {
            CommitReorder();
            _pressedIndex = -1;
            _pressIndex = -1;
            e.Handled = true;
            return;
        }

        ReleaseMouseQuietly();

        var point = e.GetPosition(RootCanvas);
        var index = HitItem(point);
        var pressed = _pressedIndex;
        _pressedIndex = -1;
        _pressIndex = -1;

        if (index >= 0 && index == pressed)
        {
            Launch(_cells[index].Item);
            e.Handled = true;
        }

        UpdateTargets(_pointerInside ? point.X : double.NaN);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        // Alt+Tab or another window stole the capture: drop the pending drag instead of
        // leaving the icons in their preview positions.
        if (_reorderActive)
        {
            _reorderActive = false;
            _reorderPreview = null;
            _pressIndex = -1;
            RebuildLayout();
        }
    }

    private void CaptureMouseQuietly()
    {
        try
        {
            CaptureMouse();
        }
        catch
        {
        }
    }

    private void ReleaseMouseQuietly()
    {
        try
        {
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }
        catch
        {
        }
    }

    #region reorder by dragging

    private const double DragThreshold = 5.0;

    private int _pressIndex = -1;
    private Point _pressPoint;
    private bool _reorderActive;
    private int _reorderTarget;
    private List<int>? _reorderPreview;

    private void BeginReorder()
    {
        _reorderActive = true;
        _reorderTarget = _pressIndex;
        _reorderPreview = null;

        ResetMagnification();
        HideChip();

        try
        {
            CaptureMouse();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Builds the prospective order (the dragged item moved to the hovered slot) and lays the
    /// icons out accordingly, while the dragged one follows the pointer.
    /// </summary>
    private void UpdateReorderPreview(Point pointer)
    {
        if (_pressIndex < 0 || _pressIndex >= _cells.Count)
        {
            return;
        }

        var count = _cells.Count;
        var target = SlotForX(pointer.X);
        _reorderTarget = target;

        var order = Enumerable.Range(0, count).ToList();
        order.Remove(_pressIndex);
        order.Insert(Math.Clamp(target, 0, order.Count), _pressIndex);
        _reorderPreview = order;

        for (var i = 0; i < count; i++)
        {
            var cell = _cells[i];

            if (i == _pressIndex)
            {
                var left = Math.Clamp(
                    pointer.X - _layout.IconSize / 2,
                    _layout.PlateLeft + _layout.PaddingX,
                    _layout.PlateLeft + _layout.PlateWidth - _layout.PaddingX - _layout.IconSize);

                Canvas.SetLeft(cell.Root, left);
                cell.Scale.ScaleX = 1.08;
                cell.Scale.ScaleY = 1.08;
                cell.Root.Opacity = 0.92;
                Panel.SetZIndex(cell.Root, 100);
            }
            else
            {
                var slot = order.IndexOf(i);
                Canvas.SetLeft(cell.Root, _layout.ItemLeft(Math.Max(0, slot)));
                cell.Scale.ScaleX = 1.0;
                cell.Scale.ScaleY = 1.0;
                cell.Root.Opacity = 1.0;
                Panel.SetZIndex(cell.Root, 0);
            }
        }
    }

    private int SlotForX(double x)
    {
        var count = Math.Max(1, _cells.Count);
        var relative = x - (_layout.PlateLeft + _layout.PaddingX) - _layout.IconSize / 2;
        var slot = (int)Math.Round(relative / _layout.Pitch);
        return Math.Clamp(slot, 0, count - 1);
    }

    private void CommitReorder()
    {
        var order = _reorderPreview;
        _reorderActive = false;
        _reorderPreview = null;

        try
        {
            ReleaseMouseCapture();
        }
        catch
        {
        }

        if (order is not null && order.Count == _cells.Count)
        {
            var newOrder = order.Select(i => _cells[i].Item).ToList();
            var changed = false;
            for (var i = 0; i < newOrder.Count && i < _cells.Count; i++)
            {
                if (!ReferenceEquals(newOrder[i], _cells[i].Item))
                {
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                Log.Info("Dock 图标顺序已调整：" + string.Join(" > ", newOrder.Select(i => i.Name)));
                ItemsReordered?.Invoke(newOrder);
                return;
            }
        }

        // Nothing moved: restore the plain layout.
        RebuildLayout();
    }

    #endregion

    #region drop from Explorer

    private void OnDragEnter(object sender, DragEventArgs e) => OnDragOver(sender, e);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accepted = CanAcceptDrop(e.Data);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (accepted)
        {
            var point = e.GetPosition(RootCanvas);
            ShowChipText("松开即可添加到 Dock", Math.Clamp(point.X, 60, Math.Max(60, _layout.WindowWidth - 60)));
        }
        else
        {
            HideChip();
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e) => HideChip();

    private void OnDrop(object sender, DragEventArgs e)
    {
        HideChip();
        e.Handled = true;

        var added = new List<DockItem>();
        foreach (var path in GetDroppedPaths(e.Data))
        {
            var item = DockItemFactory.FromPath(path);
            if (item is null)
            {
                Log.Warn($"拖入的路径无法识别：{path}");
                continue;
            }

            if (_settings.Items.Any(existing =>
                    string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            added.Add(item);
        }

        if (added.Count == 0)
        {
            ShowToast("没有可添加的项目（类型不支持，或已在 Dock 中）。");
            return;
        }

        Log.Info("从资源管理器拖入：" + string.Join(", ", added.Select(a => a.Name)));
        ItemsDropped?.Invoke(added);
    }

    private static bool CanAcceptDrop(IDataObject? data) => GetDroppedPaths(data).Count > 0;

    private static List<string> GetDroppedPaths(IDataObject? data)
    {
        var result = new List<string>();
        try
        {
            if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return result;
            }

            if (data.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return result;
            }

            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                {
                    result.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("解析拖入内容失败。", ex);
        }

        return result;
    }

    #endregion

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var point = e.GetPosition(RootCanvas);
        var index = HitItem(point);

        e.Handled = true;

        if (index < 0)
        {
            // Empty part of the capsule: offer the dock menu (settings, toggles, exit).
            var screenPoint = _source is not null
                ? PointToScreen(point)
                : point;
            DockContextRequested?.Invoke(screenPoint);
            return;
        }

        ItemContextRequested?.Invoke(_cells[index].Item, point);
    }

    private int HitItem(Point point)
    {
        if (_hidden)
        {
            return -1;
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var size = _layout.IconSize * cell.Current;
            var bottom = _layout.ItemsTop + _layout.IconSize;
            var center = _layout.ItemCenterX(i);
            var rect = new Rect(center - size / 2, bottom - size, size, size);
            rect.Inflate(2, 2);
            if (rect.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void Launch(DockItem item)
    {
        try
        {
            var result = _launcher.Launch(item);
            if (!result.Success && result.Error is { } error)
            {
                ShowToast(error);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"启动 {item.Name} 失败。", ex);
            ShowToast($"无法启动「{item.Name}」。");
        }
    }

    #endregion

    #region motion

    private bool AutoHideEnabled => _settings.AutoHideEnabled;

    private void UpdateTargets(double cursorX)
    {
        var magnify = _settings.MagnifyEnabled && !double.IsNaN(cursorX);
        var factor = _settings.MagnifyFactor;
        var sigma = Math.Max(12.0, _layout.IconSize * 1.55);

        for (var i = 0; i < _cells.Count; i++)
        {
            double target;
            if (!magnify)
            {
                target = 1.0;
            }
            else
            {
                var distance = Math.Abs(cursorX - _layout.ItemCenterX(i));
                var g = Math.Exp(-(distance / sigma) * (distance / sigma));
                target = 1.0 + (factor - 1.0) * g;
            }

            if (i == _pressedIndex)
            {
                target *= 0.93;
            }

            _cells[i].Target = target;
        }

        if (InputDebug)
        {
            Log.Info("[motion] targets " + string.Join(",", _cells.Select(c => c.Target.ToString("0.00"))));
        }

        EnsureFrames();
        EnsureVerifyTimer();
    }

    private void ResetMagnification()
    {
        _pressedIndex = -1;
        foreach (var cell in _cells)
        {
            cell.Target = 1.0;
        }

        EnsureFrames();
    }

    /// <summary>
    /// Starts the single motion loop. A DispatcherTimer at render priority is used instead
    /// of CompositionTarget.Rendering: the latter only ticks when WPF already has a reason
    /// to render, which means the very first frame of a hover animation can never arrive on
    /// an otherwise idle layered window.
    /// </summary>
    private void EnsureFrames()
    {
        if (_renderingHooked)
        {
            return;
        }

        _lastTick = Stopwatch.GetTimestamp();
        _renderingHooked = true;

        // Drive the animation from the composition frame: the update cadence then matches the
        // display refresh rate (144 Hz panels exposed the old 15 ms timer as visible judder).
        CompositionTarget.Rendering += OnFrame;

        // An idle layered window has nothing to render, so the first Rendering callback would
        // never arrive: dirty the visuals once to get the loop started.
        PlateImage.InvalidateVisual();
        ItemLayer.InvalidateVisual();
    }

    private void StopFrames()
    {
        if (!_renderingHooked)
        {
            return;
        }

        _renderingHooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var rawDt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        var dt = Math.Clamp(rawDt, 0.001, 0.05);

        if (InputDebug)
        {
            _frameSamples++;
            _frameIntervalSum += rawDt * 1000;
            if (_frameSamples >= 60)
            {
                Log.Info($"[motion] 帧间隔 {_frameIntervalSum / _frameSamples:0.0} ms（{1000 / (_frameIntervalSum / _frameSamples):0} fps，跟随屏幕刷新率）");
                _frameSamples = 0;
                _frameIntervalSum = 0;
            }
        }

        var settled = true;

        var iconK = 1 - Math.Exp(-dt / MotionTau);
        foreach (var cell in _cells)
        {
            var delta = cell.Target - cell.Current;
            if (Math.Abs(delta) > 0.0008)
            {
                cell.Current += delta * iconK;
                settled = false;
            }
            else
            {
                cell.Current = cell.Target;
            }

            // Touch the transform only when the value really moved: every write invalidates
            // the visual and forces a full software re-composite of the layered window.
            if (Math.Abs(cell.Scale.ScaleX - cell.Current) > 0.0005)
            {
                cell.Scale.ScaleX = cell.Current;
                cell.Scale.ScaleY = cell.Current;
            }
        }

        var slideK = 1 - Math.Exp(-dt / 0.085);
        var offsetDelta = _offsetTarget - _offsetCurrent;
        if (Math.Abs(offsetDelta) > 0.35)
        {
            _offsetCurrent += offsetDelta * slideK;
            settled = false;
        }
        else
        {
            _offsetCurrent = _offsetTarget;
        }

        if (Math.Abs(DockOffset.Y - _offsetCurrent) > 0.05)
        {
            DockOffset.Y = _offsetCurrent;
        }

        var chipK = 1 - Math.Exp(-dt / 0.045);
        var chipDelta = _chipOpacityTarget - _chipOpacityCurrent;
        if (Math.Abs(chipDelta) > 0.004)
        {
            _chipOpacityCurrent += chipDelta * chipK;
            settled = false;
        }
        else
        {
            _chipOpacityCurrent = _chipOpacityTarget;
        }

        if (Chip.Visibility == Visibility.Visible)
        {
            Chip.Opacity = _chipOpacityCurrent;
            if (_chipOpacityCurrent < 0.01 && _chipOpacityTarget < 0.01)
            {
                Chip.Visibility = Visibility.Collapsed;
            }
        }

        if (settled)
        {
            if (InputDebug)
            {
                Log.Info("[motion] settled " + string.Join(",", _cells.Select(c => c.Current.ToString("0.00"))));
            }

            StopFrames();
        }
    }

    private void UpdateHover(Point point)
    {
        if (!_settings.ShowLabels)
        {
            HideChip();
            _hoverIndex = -1;
            return;
        }

        var index = _layout.SlotAt(point);
        if (index < 0 || index >= _cells.Count)
        {
            HideChip();
            _hoverIndex = -1;
            return;
        }

        _hoverIndex = index;
        ShowChip(index);
    }

    private void ShowChip(int index) => ShowChipText(_cells[index].Item.Name, _layout.ItemCenterX(index));

    /// <summary>Shows the name chip centred on <paramref name="centerX"/> (window coordinates).</summary>
    private void ShowChipText(string text, double centerX)
    {
        if (_chipText == text && Chip.Visibility == Visibility.Visible)
        {
            return;
        }

        _chipIndex = -1;
        _chipText = text;
        ChipText.Text = text;
        Chip.Visibility = Visibility.Visible;
        Chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var chipWidth = Chip.DesiredSize.Width;
        var left = centerX - chipWidth / 2;
        left = Math.Max(4, Math.Min(_layout.WindowWidth - chipWidth - 4, left));

        Canvas.SetLeft(Chip, left);
        Canvas.SetTop(Chip, _layout.ChipTop);

        _chipOpacityTarget = 1.0;
        if (_chipOpacityCurrent < 0.02)
        {
            _chipOpacityCurrent = 0.0;
        }

        EnsureFrames();
    }

    private void HideChip()
    {
        _chipIndex = -1;
        _chipText = null;
        _chipOpacityTarget = 0.0;
        EnsureFrames();
    }

    #endregion

    #region auto hide

    private void ApplyAutoHideMode()
    {
        StartProbeTimer();

        if (!_settings.AutoHideEnabled)
        {
            SetHidden(false, animate: false);
        }
        else
        {
            SetHidden(!_pointerInside, animate: false);
        }
    }

    private void SetHidden(bool hide, bool animate = true)
    {
        if (_hidden == hide)
        {
            return;
        }

        _hidden = hide;
        _offsetTarget = hide ? _layout.WindowHeight : 0;

        if (!animate)
        {
            _offsetCurrent = _offsetTarget;
            DockOffset.Y = _offsetCurrent;
        }

        if (hide)
        {
            ResetMagnification();
            HideChip();
        }

        EnsureFrames();
    }

    private void StartProbeTimer()
    {
        if (!_settings.AutoHideEnabled)
        {
            _probeTimer?.Stop();
            _probeTimer = null;
            return;
        }

        _probeTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(110),
        };

        _probeTimer.Tick -= OnProbeTick;
        _probeTimer.Tick += OnProbeTick;
        _probeTimer.Start();
    }

    private void OnProbeTick(object? sender, EventArgs e)
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                return;
            }

            var screen = _screen ?? Displays.Resolve(Displays.All(), _settings.ScreenIndex);
            var scale = _scale;
            var workBottom = screen.WorkTop + screen.WorkHeight;

            // Horizontal tolerance: a little beyond the dock so the edge strip is easy to hit.
            var withinX = pt.X >= _desiredPx.Left - (int)(24 * scale) && pt.X <= _desiredPx.Left + _desiredPx.Width + (int)(24 * scale);

            // A thin strip hugging the screen edge is the "reveal" target.
            var revealTop = workBottom - (int)(7 * scale);

            var plateTopPx = _desiredPx.Top + (int)Math.Round(_layout.PlateTop * scale);
            var plateBottomPx = _desiredPx.Top + (int)Math.Round((_layout.PlateTop + _layout.PlateHeight) * scale);
            var nearPlate = pt.Y >= plateTopPx - (int)(12 * scale) && pt.Y <= plateBottomPx + (int)(16 * scale);

            if (_hidden)
            {
                if (withinX && pt.Y >= revealTop)
                {
                    SetHidden(false);
                }

                return;
            }

            var keepVisible = withinX && (nearPlate || pt.Y >= revealTop);
            if (!keepVisible)
            {
                SetHidden(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("自动隐藏探测异常。", ex);
        }
    }

    /// <summary>
    /// Safety net for layered windows: WPF occasionally misses the leave notification when
    /// the cursor slides into a fully transparent area. While any icon is magnified we poll
    /// the cursor at 4 Hz and reset the magnifier once it is outside the dock.
    /// </summary>
    private void EnsureVerifyTimer()
    {
        var magnified = _cells.Any(c => Math.Abs(c.Current - 1.0) > 0.01 || Math.Abs(c.Target - 1.0) > 0.01);
        if (!magnified)
        {
            _verifyTimer?.Stop();
            _verifyTimer = null;
            return;
        }

        _verifyTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(260),
        };

        _verifyTimer.Tick -= OnVerifyTick;
        _verifyTimer.Tick += OnVerifyTick;
        _verifyTimer.Start();
    }

    private void OnVerifyTick(object? sender, EventArgs e)
    {
        // Automation runs drive the hover state directly; the pointer check would undo it.
        if (InputDebug)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var pt) || _source is null)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(_source.Handle, out var rect))
        {
            return;
        }

        var point = new Point((pt.X - rect.Left) / _scale, (pt.Y - rect.Top) / _scale);
        var inside = _layout.ContainsPlate(point) || (point.Y >= _layout.ItemsTop - _layout.MaxExtra && point.Y <= _layout.ItemsTop + _layout.IconSize + _layout.PaddingY);
        if (inside)
        {
            return;
        }

        _pointerInside = false;
        ResetMagnification();
        HideChip();
        _verifyTimer?.Stop();
        _verifyTimer = null;

        if (_settings.AutoHideEnabled)
        {
            SetHidden(true);
        }
    }

    #endregion

    #region running indicators / toast

    private void StartRunningTimer()
    {
        _runningTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.2),
        };

        _runningTimer.Tick -= OnRunningTick;
        _runningTimer.Tick += OnRunningTick;
        _runningTimer.Start();
    }

    private void OnRunningTick(object? sender, EventArgs e) => RefreshRunningStatesAsync();

    /// <summary>
    /// Process enumeration is a blocking shell/WMI-ish call; doing it on the UI thread caused a
    /// visible hitch every couple of seconds. It now runs on the thread pool and only the dot
    /// visibility update comes back to the dispatcher.
    /// </summary>
    private async void RefreshRunningStatesAsync()
    {
        if (_runningBusy || _disposed)
        {
            return;
        }

        _runningBusy = true;
        try
        {
            if (!_settings.ShowRunningIndicator)
            {
                ApplyRunningStates(new Dictionary<string, bool>(StringComparer.Ordinal));
                return;
            }

            var items = _settings.Items.ToArray();
            var states = await Task.Run(() => _running.Refresh(items)).ConfigureAwait(true);

            if (_disposed)
            {
                return;
            }

            ApplyRunningStates(states);
        }
        catch (Exception ex)
        {
            Log.Warn("刷新运行状态失败。", ex);
        }
        finally
        {
            _runningBusy = false;
        }
    }

    private void ApplyRunningStates(IReadOnlyDictionary<string, bool> states)
    {
        foreach (var cell in _cells)
        {
            if (cell.Dot is null)
            {
                continue;
            }

            var running = states.TryGetValue(cell.Item.Id, out var value) && value;
            var visibility = running ? Visibility.Visible : Visibility.Collapsed;
            if (cell.Dot.Visibility != visibility)
            {
                cell.Dot.Visibility = visibility;
            }
        }
    }

    private void RefreshRunningStates(bool force)
    {
        try
        {
            if (!_settings.ShowRunningIndicator)
            {
                ApplyRunningStates(new Dictionary<string, bool>(StringComparer.Ordinal));
                return;
            }

            // Synchronous variant used right after (re)building the dock, where the states must
            // be correct on the very first frame.
            ApplyRunningStates(_running.Refresh(_settings.Items));
        }
        catch (Exception ex)
        {
            Log.Warn("刷新运行状态失败。", ex);
        }
    }

    internal void ShowToast(string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ToastText.Text = message;
            Toast.Visibility = Visibility.Visible;
            Toast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var width = Math.Min(380, Toast.DesiredSize.Width);
            var left = Math.Max(4, (_layout.WindowWidth - width) / 2);
            Canvas.SetLeft(Toast, left);
            Canvas.SetTop(Toast, Math.Max(4, _layout.ChipTop - Toast.DesiredSize.Height - 6));

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(3.6),
            };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer?.Stop();
                Toast.Visibility = Visibility.Collapsed;
            };
            _toastTimer.Start();
        }));
    }

    #endregion

    #region lifetime

    public void CloseDock()
    {
        try
        {
            Close();
        }
        catch
        {
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DisposeResources();
    }

    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        StopFrames();

        _runningTimer?.Stop();
        _runningTimer = null;
        _probeTimer?.Stop();
        _probeTimer = null;
        _verifyTimer?.Stop();
        _verifyTimer = null;
        _fullscreenTimer?.Dispose();
        _fullscreenTimer = null;
        _toastTimer?.Stop();
        _toastTimer = null;

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        ItemLayer.Children.Clear();
        DotLayer.Children.Clear();
        _cells.Clear();
    }

    #endregion

    private sealed class IconCell(DockItem item, Grid root, Image image, ScaleTransform scale)
    {
        public DockItem Item { get; set; } = item;

        public Grid Root { get; } = root;

        public Image Image { get; } = image;

        public ScaleTransform Scale { get; } = scale;

        public Ellipse? Dot { get; set; }

        public bool Available { get; set; } = true;

        public double Current { get; set; } = 1.0;

        public double Target { get; set; } = 1.0;
    }

    private readonly record struct PixelRect(int Left, int Top, int Width, int Height);
}
