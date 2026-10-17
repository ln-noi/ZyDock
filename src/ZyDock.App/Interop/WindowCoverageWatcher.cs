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

            var target = info.rcWork;

            return rect.Left <= target.Left + Tolerance
                && rect.Top <= target.Top + Tolerance
                && rect.Right >= target.Right - Tolerance
                && rect.Bottom >= target.Bottom - Tolerance;
        }
        catch
        {
            return false;
        }
    }
}
