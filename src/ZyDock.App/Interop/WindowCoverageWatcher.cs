using System.Text;

namespace ZyDock.App.Interop;

/// <summary>
/// 检测"前台窗口是否已经占满桌面"，用于让 Dock 自动让位。
///
/// 判定标准是窗口覆盖显示器的**工作区**（rcWork，即任务栏上方那块区域）：
///   · 全屏应用（游戏 / 视频 / 浏览器全屏）覆盖整个工作区 → 命中
///   · 普通最大化的窗口也正好覆盖工作区 → 命中
///   · 普通窗口（含本程序的设置窗口）不覆盖 → 不命中
/// 桌面、任务栏、Shell 表面等系统窗口会被排除。
/// </summary>
internal static class WindowCoverageWatcher
{
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>允许的像素误差（DWM 边框、阴影、取整）。</summary>
    private const int Tolerance = 4;

    private static readonly string[] ShellClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
    ];

    private static readonly StringBuilder ClassBuffer = new(256);

    /// <summary>前台窗口是否最大化/全屏地占满了该显示器的工作区。</summary>
    public static bool CoversDesktop(IntPtr monitorHandle, IntPtr ownWindow)
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == ownWindow)
            {
                return false;
            }

            if (!NativeMethods.IsWindowVisible(foreground))
            {
                return false;
            }

            // 桌面 / 任务栏 / Shell 表面不算"应用窗口"。
            ClassBuffer.Clear();
            var length = NativeMethods.GetClassNameW(foreground, ClassBuffer, ClassBuffer.Capacity);
            if (length > 0)
            {
                var name = ClassBuffer.ToString();
                foreach (var shellClass in ShellClasses)
                {
                    if (string.Equals(name, shellClass, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            if (!NativeMethods.GetWindowRect(foreground, out var rect))
            {
                return false;
            }

            // 只关心 Dock 所在的那块屏幕。
            var monitor = NativeMethods.MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            if (monitorHandle != IntPtr.Zero && monitor != monitorHandle)
            {
                return false;
            }

            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            };

            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            // 1) Authoritative answer for "maximized": ask Windows for the window's placement.
            //    Comparing rectangles alone breaks as soon as the taskbar is set to auto-hide,
            //    because then the work area is the whole screen while a maximized window stops
            //    a few pixels above the bottom edge.
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
            };

            if (NativeMethods.GetWindowPlacement(foreground, ref placement))
            {
                const int swShowMaximized = 3;
                if (placement.showCmd == swShowMaximized)
                {
                    return true;
                }
            }

            // 2) Borderless fullscreen (games / video players) has no "maximized" state, so fall
            //    back to geometry: cover the work area, or the whole monitor.
            if (Covers(rect, info.rcWork))
            {
                return true;
            }

            if (Covers(rect, info.rcMonitor))
            {
                return true;
            }

            // 3) Split screen: neither window covers everything on its own, but together they do.
            //    Cover the monitor with a coarse grid and count the cells touched by any visible,
            //    non-minimized top-level window of another process.
            return IsScreenCoveredByWindows(info.rcMonitor, ownWindow, info);
        }
        catch
        {
            return false;
        }



        static bool Covers(NativeMethods.RECT window, NativeMethods.RECT target)
        {
            return window.Left <= target.Left + Tolerance
                && window.Top <= target.Top + Tolerance
                && window.Right >= target.Right - Tolerance
                && window.Bottom >= target.Bottom - Tolerance;
        }
    }


    /// <summary>
    /// True when visible application windows together cover (almost) the whole monitor: what
    /// two snapped windows look like. Neither is maximized or fullscreen, so the per-window
    /// tests above miss that case entirely.
    /// </summary>
    private static bool IsScreenCoveredByWindows(NativeMethods.RECT monitor, IntPtr ownWindow, NativeMethods.MONITORINFOEX info)
    {
        const int columns = 24;
        const int rows = 18;
        const double threshold = 0.92;

        var width = monitor.Right - monitor.Left;
        var height = monitor.Bottom - monitor.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var cellWidth = (double)width / columns;
        var cellHeight = (double)height / rows;
        var covered = new bool[columns * rows];
        var any = false;
        var ownPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == ownWindow || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            if (NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == ownPid)
            {
                return true;
            }

            // Tool windows, popups and shell surfaces are not "the app the user is working in".
            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            const long wsExToolWindow = 0x00000080L;
            if ((exStyle & wsExToolWindow) != 0)
            {
                return true;
            }

            const int dwmwaCloaked = 14;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, dwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            ClassBuffer.Clear();
            var length = NativeMethods.GetClassNameW(hwnd, ClassBuffer, ClassBuffer.Capacity);
            if (length > 0)
            {
                var name = ClassBuffer.ToString();
                foreach (var shellClass in ShellClasses)
                {
                    if (string.Equals(name, shellClass, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            // Ignore tiny overlays (toolbars, toasts) so they never count as coverage.
            var left = Math.Max(rect.Left, monitor.Left);
            var top = Math.Max(rect.Top, monitor.Top);
            var right = Math.Min(rect.Right, monitor.Right);
            var bottom = Math.Min(rect.Bottom, monitor.Bottom);
            if (right - left < width / 8 || bottom - top < height / 8)
            {
                return true;
            }

            any = true;

            var fromColumn = Math.Max(0, (int)Math.Floor((left - monitor.Left) / cellWidth));
            var toColumn = Math.Min(columns - 1, (int)Math.Ceiling((right - monitor.Left) / cellWidth) - 1);
            var fromRow = Math.Max(0, (int)Math.Floor((top - monitor.Top) / cellHeight));
            var toRow = Math.Min(rows - 1, (int)Math.Ceiling((bottom - monitor.Top) / cellHeight) - 1);

            for (var row = fromRow; row <= toRow; row++)
            {
                var cellRow = row * columns;
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    covered[cellRow + column] = true;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (!any)
        {
            return false;
        }

        var hit = 0;
        foreach (var cell in covered)
        {
            if (cell)
            {
                hit++;
            }
        }

        return (double)hit / covered.Length >= threshold;
    }
}
