using System.Runtime.InteropServices;
using System.Windows.Interop;
using ZyDock.Core.Diagnostics;

namespace ZyDock.App.Interop;

/// <summary>
/// Notify icon built directly on Shell_NotifyIcon with a message-only window, so the
/// dock can live in the tray without pulling WinForms into the design language.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int IDI_APPLICATION = 32512;
    private const int NIM_SETVERSION = 0x00000004;
    private const int NOTIFYICON_VERSION_4 = 4;

    private HwndSource? _source;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;
    private string _tooltip = "ZyDock";

    public event EventHandler? LeftClick;

    public event EventHandler? RightClick;

    public bool Create(string? iconPath, string tooltip)
    {
        try
        {
            _tooltip = tooltip;

            var parameters = new HwndSourceParameters("ZyDockTray")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);

            _icon = LoadIconFromFile(iconPath);

            var data = BuildData();
            _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data) != 0;
            if (!_added)
            {
                Log.Warn($"添加托盘图标失败（cbSize={data.cbSize}, win32={Marshal.GetLastWin32Error()}）。");
                return false;
            }

            var version = data;
            version.uVersion = NOTIFYICON_VERSION_4;
            NativeMethods.Shell_NotifyIcon(NIM_SETVERSION, ref version);
            UpdateTooltip(_tooltip);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("创建托盘图标异常。", ex);
            return false;
        }
    }

    public void UpdateTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (!_added)
        {
            return;
        }

        try
        {
            var data = BuildData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn("更新托盘提示失败。", ex);
        }
    }

    private NativeMethods.NOTIFYICONDATA BuildData()
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _source?.Handle ?? IntPtr.Zero,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = _icon,
            szTip = Truncate(_tooltip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = Guid.Empty,
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_TRAYICON)
        {
            return IntPtr.Zero;
        }

        var notification = (int)(lParam.ToInt64() & 0xFFFF);
        switch (notification)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                handled = true;
                LeftClick?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                handled = true;
                RightClick?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    private static IntPtr LoadIconFromFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var handle = NativeMethods.LoadImage(
                IntPtr.Zero,
                path,
                NativeMethods.IMAGE_ICON,
                0,
                0,
                NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);

            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        // Single file publish: the icon ships inside the executable, so take it from there.
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            try
            {
                if (ExtractIconExW(executable, 0, out var large, out var small, 1) > 0)
                {
                    if (small != IntPtr.Zero)
                    {
                        if (large != IntPtr.Zero)
                        {
                            NativeMethods.DestroyIcon(large);
                        }

                        return small;
                    }

                    if (large != IntPtr.Zero)
                    {
                        return large;
                    }
                }
            }
            catch
            {
            }
        }

        return LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_added)
            {
                var data = BuildData();
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
                _added = false;
            }
        }
        catch
        {
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);
}
