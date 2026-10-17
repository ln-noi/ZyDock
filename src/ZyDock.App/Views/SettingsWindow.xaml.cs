using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZyDock.App.Interop;
using ZyDock.Core.Diagnostics;
using ZyDock.Core.Models;
using ZyDock.Core.Services;

namespace ZyDock.App.Views;

/// <summary>
/// Settings surface. Same glass language as the dock: a rounded, translucent window with
/// custom switches and sliders instead of stock Windows chrome. Every change is applied
/// live (debounced) and persisted by the host.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly IconProvider _icons;
    private readonly AppLauncher _launcher;
    private readonly AutoStartService _autoStart;

    private DockSettings _settings;
    private DispatcherTimer? _debounce;
    private bool _suppress;

    public SettingsWindow(
        SettingsService settingsService,
        IconProvider icons,
        AppLauncher launcher,
        AutoStartService autoStart,
        DockSettings settings)
    {
        _settingsService = settingsService;
        _icons = icons;
        _launcher = launcher;
        _autoStart = autoStart;
        _settings = settings.Clone();

        InitializeComponent();

        // Cap the height so the window stays comfortable on tall screens (the body scrolls).
        MaxHeight = Math.Min(940, Math.Max(480, SystemParameters.WorkArea.Height - 120));
        MaxWidth = Math.Max(700, SystemParameters.WorkArea.Width - 80);

        Loaded += (_, _) => LoadFromSettings(_settings, rebuildList: true);
        BuildStaticLabels();
        WireEvents();
    }

    public event Action<DockSettings>? SettingsChanged;

    public event Action? ExitRequested;

    #region wiring

    private void BuildStaticLabels()
    {
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = $"ZyDock {version?.ToString(3) ?? "1.0.0"} · Windows 玻璃风格 Dock";
        ConfigPathText.Text = $"配置：{_settingsService.FilePath}";
        LogPathText.Text = $"日志：{Log.FilePath}";
    }

    private void WireEvents()
    {
        IconSizeSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.IconSize = Math.Round(IconSizeSlider.Value);
            IconSizeValue.Text = $"{_settings.IconSize:0} px";
            ScheduleApply();
        };

        SpacingSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.IconSpacing = Math.Round(SpacingSlider.Value);
            SpacingValue.Text = $"{_settings.IconSpacing:0} px";
            ScheduleApply();
        };

        OpacitySlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.PanelOpacity = Math.Round(OpacitySlider.Value, 3);
            OpacityValue.Text = $"{_settings.PanelOpacity * 100:0}%";
            ScheduleApply();
        };

        PaddingXSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.PaddingHorizontal = Math.Round(PaddingXSlider.Value);
            PaddingXValue.Text = $"{_settings.PaddingHorizontal:0} px";
            ScheduleApply();
        };

        PaddingYSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.PaddingVertical = Math.Round(PaddingYSlider.Value);
            PaddingYValue.Text = $"{_settings.PaddingVertical:0} px";
            ScheduleApply();
        };

        BorderSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.BorderThickness = Math.Round(BorderSlider.Value, 2);
            BorderValue.Text = _settings.BorderThickness <= 0.01
                ? "无边框"
                : $"{_settings.BorderThickness:0.#} px";
            ScheduleApply();
        };

        MagnifySlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.MagnifyFactor = Math.Round(MagnifySlider.Value, 2);
            MagnifyValue.Text = $"{_settings.MagnifyFactor:0.00}×";
            ScheduleApply();
        };

        EdgeOffsetSlider.ValueChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _settings.EdgeOffset = Math.Round(EdgeOffsetSlider.Value);
            EdgeOffsetValue.Text = $"{_settings.EdgeOffset:0} px";
            ScheduleApply();
        };

        GlassToggle.Checked += (_, _) => OnToggleChanged(() => _settings.GlassEnabled = true);
        GlassToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.GlassEnabled = false);

        MagnifyToggle.Checked += (_, _) => OnToggleChanged(() => _settings.MagnifyEnabled = true);
        MagnifyToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.MagnifyEnabled = false);

        LabelsToggle.Checked += (_, _) => OnToggleChanged(() => _settings.ShowLabels = true);
        LabelsToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.ShowLabels = false);

        IndicatorToggle.Checked += (_, _) => OnToggleChanged(() => _settings.ShowRunningIndicator = true);
        IndicatorToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.ShowRunningIndicator = false);

        FullscreenToggle.Checked += (_, _) => OnToggleChanged(() => _settings.HideOnFullscreen = true);
        FullscreenToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.HideOnFullscreen = false);

        ShadowToggle.Checked += (_, _) => OnToggleChanged(() => _settings.ShadowEnabled = true);
        ShadowToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.ShadowEnabled = false);

        TintHexBox.LostFocus += (_, _) => ApplyTintFromBox();
        TintHexBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { ApplyTintFromBox(); e.Handled = true; }
        };

        AutoHideToggle.Checked += (_, _) => OnToggleChanged(() => _settings.AutoHideEnabled = true);
        AutoHideToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.AutoHideEnabled = false);

        HideTaskbarToggle.Checked += (_, _) => OnToggleChanged(() => _settings.HideSystemTaskbar = true);
        HideTaskbarToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.HideSystemTaskbar = false);

        AutoStartToggle.Checked += (_, _) => OnToggleChanged(() => _settings.AutoStartEnabled = true);
        AutoStartToggle.Unchecked += (_, _) => OnToggleChanged(() => _settings.AutoStartEnabled = false);
    }

    private void OnToggleChanged(Action mutate)
    {
        if (_suppress)
        {
            return;
        }

        mutate();
        ScheduleApply();
    }

    /// <summary>Clicking a colour swatch selects that glass colour.</summary>
    private void OnTintSwatchClick(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not FrameworkElement element || element.Tag is not string hex)
        {
            return;
        }

        _settings.TintColor = DockSettings.NormalizeColor(hex);
        TintHexBox.Text = _settings.TintColor;
        ScheduleApply();
    }

    /// <summary>Applies whatever was typed into the hex box; invalid input falls back to the current colour.</summary>
    private void ApplyTintFromBox()
    {
        if (_suppress)
        {
            return;
        }

        var normalized = DockSettings.NormalizeColor(TintHexBox.Text);
        if (string.Equals(normalized, TintHexBox.Text, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TintHexBox.Text = _settings.TintColor;
        if (string.Equals(normalized, _settings.TintColor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.TintColor = normalized;
        TintHexBox.Text = normalized;
        ScheduleApply();
    }

    private void ScheduleApply()
    {
        _debounce ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };

        _debounce.Tick -= OnDebounceTick;
        _debounce.Tick += OnDebounceTick;
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        SettingsChanged?.Invoke(_settings.Clone());
    }

    private void ApplyNow()
    {
        _debounce?.Stop();
        SettingsChanged?.Invoke(_settings.Clone());
    }

    #endregion

    #region load

    /// <summary>Refreshes the window from an external settings snapshot (tray toggles, removal).</summary>
    public void ReloadFromSettings(DockSettings settings)
    {
        _settings = settings.Clone();
        LoadFromSettings(_settings, rebuildList: true);
    }

    private void LoadFromSettings(DockSettings settings, bool rebuildList)
    {
        _suppress = true;
        try
        {
            IconSizeSlider.Value = settings.IconSize;
            SpacingSlider.Value = settings.IconSpacing;
            OpacitySlider.Value = settings.PanelOpacity;
            MagnifySlider.Value = settings.MagnifyFactor;
            BorderSlider.Value = settings.BorderThickness;
            PaddingXSlider.Value = settings.PaddingHorizontal;
            PaddingYSlider.Value = settings.PaddingVertical;
            EdgeOffsetSlider.Value = settings.EdgeOffset;

            IconSizeValue.Text = $"{settings.IconSize:0} px";
            SpacingValue.Text = $"{settings.IconSpacing:0} px";
            OpacityValue.Text = $"{settings.PanelOpacity * 100:0}%";
            MagnifyValue.Text = $"{settings.MagnifyFactor:0.00}×";
            BorderValue.Text = settings.BorderThickness <= 0.01
                ? "无边框"
                : $"{settings.BorderThickness:0.#} px";
            PaddingXValue.Text = $"{settings.PaddingHorizontal:0} px";
            PaddingYValue.Text = $"{settings.PaddingVertical:0} px";
            EdgeOffsetValue.Text = $"{settings.EdgeOffset:0} px";

            GlassToggle.IsChecked = settings.GlassEnabled;
            MagnifyToggle.IsChecked = settings.MagnifyEnabled;
            LabelsToggle.IsChecked = settings.ShowLabels;
            IndicatorToggle.IsChecked = settings.ShowRunningIndicator;
            AutoHideToggle.IsChecked = settings.AutoHideEnabled;
            ShadowToggle.IsChecked = settings.ShadowEnabled;
            TintHexBox.Text = settings.TintColor;
            FullscreenToggle.IsChecked = settings.HideOnFullscreen;
            HideTaskbarToggle.IsChecked = settings.HideSystemTaskbar;
            AutoStartToggle.IsChecked = settings.AutoStartEnabled;

            MagnifySlider.IsEnabled = settings.MagnifyEnabled;

            BuildScreenButtons();
        }
        finally
        {
            _suppress = false;
        }

        if (rebuildList)
        {
            BuildIconList();
        }
    }

    private void BuildScreenButtons()
    {
        ScreenPanel.Children.Clear();

        var screens = Displays.All();
        var selected = _settings.ScreenIndex;

        for (var i = 0; i < screens.Count; i++)
        {
            var index = i;
            var screen = screens[i];
            var label = screen.IsPrimary
                ? $"主屏幕 · {screen.Width}×{screen.Height}"
                : $"{screen.DeviceName} · {screen.Width}×{screen.Height}";
            if (screen.Scale > 1.01)
            {
                label += $" @{screen.Scale * 100:0}%";
            }

            var radio = new RadioButton
            {
                Content = label,
                GroupName = "ScreenSelect",
                Style = (Style)FindResource("SegmentRadio"),
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = selected < 0 ? screen.IsPrimary : selected == index,
            };

            radio.Checked += (_, _) =>
            {
                if (_suppress)
                {
                    return;
                }

                _settings.ScreenIndex = index;
                ScheduleApply();
            };

            ScreenPanel.Children.Add(radio);
        }
    }

    private void BuildIconList()
    {
        IconListPanel.Children.Clear();

        if (_settings.Items.Count == 0)
        {
            IconEmptyHint.Visibility = Visibility.Visible;
            return;
        }

        IconEmptyHint.Visibility = Visibility.Collapsed;

        for (var i = 0; i < _settings.Items.Count; i++)
        {
            IconListPanel.Children.Add(BuildIconRow(_settings.Items[i], i));
        }
    }

    private UIElement BuildIconRow(DockItem item, int index)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image
        {
            Source = _icons.GetIcon(item),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var texts = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = item.Name,
            Style = (Style)FindResource("LabelText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var subtitle = item.Kind == LaunchKind.UwpApp
            ? "应用商店应用"
            : string.IsNullOrWhiteSpace(item.Target) ? "未设置路径" : item.Target;
        texts.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = (Style)FindResource("HintText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
            MaxWidth = 360,
        });

        if (!_launcher.IsReachable(item))
        {
            texts.Children.Add(new TextBlock
            {
                Text = "路径无效，图标会以占位形式显示",
                Style = (Style)FindResource("HintText"),
                Foreground = (Brush)FindResource("DangerBrush"),
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        actions.Children.Add(SmallButton("▲", "上移", () => MoveItem(index, -1), enabled: index > 0, width: 34));
        actions.Children.Add(SmallButton("▼", "下移", () => MoveItem(index, 1), enabled: index < _settings.Items.Count - 1, width: 34));

        if (item.Kind is LaunchKind.Application or LaunchKind.Url or LaunchKind.ShellUri)
        {
            actions.Children.Add(SmallButton("应用", "更换应用路径", () => ChangeTarget(index), width: 46));
        }

        if (item.Kind == LaunchKind.Folder)
        {
            actions.Children.Add(SmallButton("文件夹", "更换文件夹", () => ChangeFolder(index), width: 54));
        }

        actions.Children.Add(SmallButton("✕", "从 Dock 移除", () => RemoveItem(index), width: 30));

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 5, 6, 5),
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            Child = grid,
        };
    }

    private Button SmallButton(string content, string tooltip, Action action, bool enabled = true, double width = 30)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Style = (Style)FindResource("GhostButton"),
            Width = width,
            Height = 28,
            Padding = new Thickness(0),
            FontSize = 11.5,
            IsEnabled = enabled,
        };

        button.Click += (_, _) =>
        {
            action();
            BuildIconList();
            ApplyNow();
        };

        return button;
    }

    #endregion

    #region icon editing

    private void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加到 Dock 的应用",
            Filter = "应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat;*.cmd|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var item = DockItemFactory.FromPath(dialog.FileName);
        if (item is null)
        {
            MessageBox.Show(this, "无法识别该路径。", "ZyDock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Items.Add(item);
        BuildIconList();
        ApplyNow();
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要添加到 Dock 的文件夹",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var item = DockItemFactory.FromPath(dialog.FolderName);
        if (item is null)
        {
            return;
        }

        _settings.Items.Add(item);
        BuildIconList();
        ApplyNow();
    }

    private void ChangeTarget(int index)
    {
        if (index < 0 || index >= _settings.Items.Count)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用或快捷方式",
            Filter = "应用程序 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var replacement = DockItemFactory.FromPath(dialog.FileName);
        if (replacement is null)
        {
            return;
        }

        var item = _settings.Items[index];
        item.Target = replacement.Target;
        item.Kind = replacement.Kind;
        item.ProcessName = replacement.ProcessName;
        item.Name = replacement.Name;
        _icons.Invalidate(item);
    }

    private void ChangeFolder(int index)
    {
        if (index < 0 || index >= _settings.Items.Count)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择文件夹" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var item = _settings.Items[index];
        item.Target = dialog.FolderName;
        item.Name = new DirectoryInfo(dialog.FolderName).Name;
        item.Kind = LaunchKind.Folder;
        _icons.Invalidate(item);
    }

    private void MoveItem(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= _settings.Items.Count || target < 0 || target >= _settings.Items.Count)
        {
            return;
        }

        (_settings.Items[index], _settings.Items[target]) = (_settings.Items[target], _settings.Items[index]);
    }

    private void RemoveItem(int index)
    {
        if (index < 0 || index >= _settings.Items.Count)
        {
            return;
        }

        _settings.Items.RemoveAt(index);
    }

    private void OnResetIconsClick(object sender, RoutedEventArgs e)
    {
        _settings.Items = LaunchCatalog.CreateDefaultItems();
        _icons.Clear();
        BuildIconList();
        ApplyNow();
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "将把 Dock 大小、外观、行为以及图标列表全部恢复为默认值，是否继续？",
            "恢复默认设置",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _icons.Clear();
        _settings = new DockSettings
        {
            Items = LaunchCatalog.CreateDefaultItems(),
        }.Clamp();

        LoadFromSettings(_settings, rebuildList: true);
        ApplyNow();
    }

    private void OnOpenConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settingsService.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("打开配置目录失败。", ex);
        }
    }

    #endregion

    #region window chrome

    private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "确定要退出 ZyDock 吗？退出后将恢复 Windows 任务栏状态。", "退出 ZyDock",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        ExitRequested?.Invoke();
    }

    #endregion
}
