using System.Windows;
using System.Windows.Threading;
using ZyDock.App.Interop;
using ZyDock.App.Views;
using ZyDock.Core.Diagnostics;
using ZyDock.Core.Models;
using ZyDock.Core.Services;

namespace ZyDock.App;

/// <summary>
/// Composition root: owns the long lived services, the dock window, the tray icon and
/// the taskbar state. Everything that is created here is also torn down on exit.
/// </summary>
public partial class App : Application
{
    private SettingsService _settingsService = null!;
    private IconProvider _icons = null!;
    private AppLauncher _launcher = null!;
    private RunningAppTracker _running = null!;
    private AutoStartService _autoStart = null!;
    private SingleInstance _singleInstance = null!;

    private TrayIcon? _tray;
    private DockWindow? _dock;
    private SettingsWindow? _settingsWindow;
    private bool _taskbarHiddenByUs;
    private bool _shuttingDown;

    private DockSettings Settings => _settingsService.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Info("=== ZyDock OnStartup ===");
        Log.Info($"LocalApplicationData={Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
        Log.Info($"ApplicationData={Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}");
        Log.Info($"ProcessPath={Environment.ProcessPath}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = new SingleInstance();
        if (!_singleInstance.TryAcquire())
        {
            Log.Info("检测到已运行的 ZyDock 实例，本次启动退出。");
            Shutdown(0);
            return;
        }

        Log.Info("单实例检查通过。");

        _singleInstance.ActivationRequested += () => Dispatcher.BeginInvoke(new Action(ShowSettings));

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        Log.Info($"配置载入完成，首次运行={_settingsService.IsFirstRun}，图标数={settings.Items.Count}");

        _icons = new IconProvider();
        _launcher = new AppLauncher();
        _running = new RunningAppTracker();
        _autoStart = new AutoStartService();

        // Keep the registry entry in sync with the saved preference.
        if (settings.AutoStartEnabled != _autoStart.IsEnabled())
        {
            _autoStart.SetEnabled(settings.AutoStartEnabled);
        }

        ApplyTaskbarPreference(settings);
        Log.Info("任务栏偏好已应用。");

        _dock = new DockWindow(_settingsService, _icons, _launcher, _running);
        _dock.ItemContextRequested += OnItemContextRequested;
        _dock.DockContextRequested += OnDockContextRequested;
        _dock.ItemsDropped += OnItemsDropped;
        _dock.ItemsReordered += OnItemsReordered;
        _dock.Show();
        Log.Info("Dock 窗口已显示。");

        var trayReady = CreateTray();
        Log.Info(trayReady
            ? "托盘图标已创建。"
            : "托盘图标不可用（shell 拒绝了请求）；仍可通过再次启动 exe 或右键 Dock 空白处打开设置。");

        if (_settingsService.IsFirstRun)
        {
            _settingsService.Save(settings);
            Log.Info("首次运行，已写入默认配置。");
        }

        if (_settingsService.LoadWarning is { } warning)
        {
            _dock.ShowToast(warning);
        }

        if (e.Args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            ShowSettings();
        }

        Log.Info($"ZyDock 已启动（{Environment.ProcessPath}）。");
    }

    private bool CreateTray()
    {
        _tray = new TrayIcon();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "App.ico");
        }

        var created = _tray.Create(iconPath, "ZyDock");
        _tray.LeftClick += (_, _) => ShowSettings();
        _tray.RightClick += (_, _) => ShowTrayMenu();
        return created;
    }

    #region settings window

    internal void ShowSettings()
    {
        try
        {
            Log.Info("打开设置窗口。");
            if (_settingsWindow is not null)
            {
                _settingsWindow.ReloadFromSettings(Settings);
                if (!_settingsWindow.IsVisible)
                {
                    _settingsWindow.Show();
                }

                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }

            _settingsWindow = new SettingsWindow(_settingsService, _icons, _launcher, _autoStart, Settings);
            _settingsWindow.SettingsChanged += OnSettingsChanged;
            _settingsWindow.ExitRequested += ExitApp;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("打开设置窗口失败。", ex);
        }
    }

    private void OnSettingsChanged(DockSettings settings)
    {
        try
        {
            _settingsService.Save(settings);
            _dock?.ApplySettings(settings, rebuildItems: true);
            ApplyTaskbarPreference(settings);

            if (settings.AutoStartEnabled != _autoStart.IsEnabled())
            {
                _autoStart.SetEnabled(settings.AutoStartEnabled);
            }
        }
        catch (Exception ex)
        {
            Log.Error("应用设置失败。", ex);
        }
    }

    private void ApplyTaskbarPreference(DockSettings settings)
    {
        if (settings.HideSystemTaskbar)
        {
            if (TaskbarAutoHide.SetAutoHide(true))
            {
                _taskbarHiddenByUs = true;

                // Explorer does not always apply the change immediately; give it a moment and
                // then re-place the dock for the (possibly) new work area. If the taskbar is
                // still visible the dock simply sits above it - never on top of it.
                ScheduleReposition();
            }
        }
        else if (_taskbarHiddenByUs)
        {
            TaskbarAutoHide.SetAutoHide(false);
            _taskbarHiddenByUs = false;
            ScheduleReposition();
        }
    }

    private void ScheduleReposition()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(900),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                _dock?.RefreshPlacement();
                Log.Info("已按当前工作区重新定位 Dock。");
            }
            catch (Exception ex)
            {
                Log.Warn("重新定位 Dock 失败。", ex);
            }
        };
        timer.Start();
    }

    #endregion

    #region menus

    private void ShowTrayMenu()
    {
        NativeMethods.GetCursorPos(out var pt);
        PopupMenuWindow.ShowAt(new Point(pt.X, pt.Y), BuildMainMenu());
    }

    private void OnItemContextRequested(DockItem item, Point dockPoint)
    {
        try
        {
            var screenPoint = _dock?.PointToScreen(dockPoint) ?? dockPoint;
            var items = new List<PopupMenuItem>
            {
                new("打开", () => _launcher.Launch(item)),
                new("在资源管理器中显示", () => RevealInExplorer(item)),
                PopupMenuItem.Separator,
                new("编辑设置…", ShowSettings),
                new("从 Dock 移除", () => RemoveItem(item), IsChecked: false, IsSeparator: false, IsDanger: true),
            };

            PopupMenuWindow.ShowAt(screenPoint, items);
        }
        catch (Exception ex)
        {
            Log.Warn("显示图标右键菜单失败。", ex);
        }
    }

    /// <summary>Files or folders dropped from Explorer: append them and persist immediately.</summary>
    private void OnItemsDropped(IReadOnlyList<DockItem> added)
    {
        try
        {
            var settings = Settings.Clone();
            var count = 0;
            foreach (var item in added)
            {
                if (settings.Items.Any(existing =>
                        string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                settings.Items.Add(item);
                count++;
            }

            if (count == 0)
            {
                return;
            }

            OnSettingsChanged(settings);
            _settingsWindow?.ReloadFromSettings(settings);
            _dock?.ShowToast(count == 1 ? $"已添加「{added[0].Name}」" : $"已添加 {count} 个图标");
        }
        catch (Exception ex)
        {
            Log.Error("处理拖入失败。", ex);
        }
    }

    /// <summary>Icons were dragged to a new position inside the dock.</summary>
    private void OnItemsReordered(IReadOnlyList<DockItem> order)
    {
        try
        {
            var settings = Settings.Clone();
            var byId = settings.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
            var reordered = new List<DockItem>(settings.Items.Count);

            foreach (var item in order)
            {
                if (byId.Remove(item.Id, out var existing))
                {
                    reordered.Add(existing);
                }
            }

            // Keep anything the dock did not know about (paranoia: never lose an entry).
            reordered.AddRange(byId.Values);
            settings.Items = reordered;

            OnSettingsChanged(settings);
            _settingsWindow?.ReloadFromSettings(settings);
        }
        catch (Exception ex)
        {
            Log.Error("保存图标顺序失败。", ex);
        }
    }

    /// <summary>Right click on the empty part of the capsule: the main dock menu.</summary>
    private void OnDockContextRequested(Point screenPoint)
    {
        try
        {
            var items = BuildMainMenu();
            PopupMenuWindow.ShowAt(screenPoint, items);
        }
        catch (Exception ex)
        {
            Log.Warn("显示 Dock 右键菜单失败。", ex);
        }
    }

    /// <summary>
    /// The right-click menu (dock capsule and tray icon). Deliberately minimal: it only routes to
    /// the settings window and quits. Every toggle lives in the settings window, so the menu does
    /// not duplicate them as shortcuts.
    /// </summary>
    private List<PopupMenuItem> BuildMainMenu()
    {
        return
        [
            new("打开设置…", ShowSettings),
            PopupMenuItem.Separator,
            new("退出 ZyDock", ExitApp, IsChecked: false, IsSeparator: false, IsDanger: true),
        ];
    }

    private void RevealInExplorer(DockItem item)
    {
        try
        {
            var target = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
            if (item.Kind == LaunchKind.Folder && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
                return;
            }

            if (File.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("在资源管理器中显示失败。", ex);
        }
    }

    private void RemoveItem(DockItem item)
    {
        var settings = Settings.Clone();
        settings.Items.RemoveAll(i => i.Id == item.Id);
        _settingsService.Save(settings);
        _dock?.ApplySettings(settings, rebuildItems: true);
        _settingsWindow?.ReloadFromSettings(settings);
    }

    private void ToggleSetting(Action<DockSettings> mutate)
    {
        var settings = Settings.Clone();
        mutate(settings);
        OnSettingsChanged(settings);
        _settingsWindow?.ReloadFromSettings(settings);
    }


    #endregion

    #region shutdown / errors

    internal void ExitApp()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        Log.Info("用户请求退出 ZyDock。");

        try
        {
            _settingsWindow?.Close();
        }
        catch
        {
        }

        try
        {
            _tray?.Dispose();
            _tray = null;
        }
        catch
        {
        }

        try
        {
            _dock?.CloseDock();
            _dock = null;
        }
        catch
        {
        }

        try
        {
            TaskbarAutoHide.Restore();
        }
        catch
        {
        }

        try
        {
            _singleInstance.Dispose();
        }
        catch
        {
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_shuttingDown)
        {
            try
            {
                _tray?.Dispose();
                TaskbarAutoHide.Restore();
                _singleInstance.Dispose();
            }
            catch
            {
            }
        }

        Log.Info("ZyDock 已退出。");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI 线程未处理异常。", e.Exception);
        e.Handled = true;

        try
        {
            _dock?.ShowToast("发生了一个内部错误，已记录到日志。");
        }
        catch
        {
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("进程未处理异常。", e.ExceptionObject as Exception);

        try
        {
            TaskbarAutoHide.Restore();
        }
        catch
        {
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Warn("后台任务异常。", e.Exception);
        e.SetObserved();
    }

    #endregion
}
