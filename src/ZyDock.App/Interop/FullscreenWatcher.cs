using System.Text;

namespace ZyDock.App.Interop;

/// <summary>
/// 检测"是否有全屏应用占用了指定显示器"，用于让 Dock 在全屏时完全隐藏。
///
/// 判定标准是窗口覆盖显示器的**完整边界**（而不是工作区）：
/// 仅仅"最大化"的窗口会停在任务栏上方，不会触发；真正全屏的游戏/视频/浏览器全屏才会。
/// </summary>
internal static class FullscreenWatcher
{
    private const uint MonitorDefaultToNearest = 2;

    private static readonly string[] ShellClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
    ];

    private static readonly StringBuilder ClassBuffer = new(256);

    /// <summary>前台窗口是否为该显示器上的全屏窗口。</summary>
    public static bool IsFullscreenOn(IntPtr monitorHandle, IntPtr ownWindow)
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

            // 桌面 / 任务栏 / Shell 表面不算"应用"。
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

            // 只关心"Dock 所在的那块屏幕"上的全屏应用。
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

            const int tolerance = 2;
            var target = info.rcMonitor;

            return rect.Left <= target.Left + tolerance
                && rect.Top <= target.Top + tolerance
                && rect.Right >= target.Right - tolerance
                && rect.Bottom >= target.Bottom - tolerance;
        }
        catch
        {
            return false;
        }
    }
}
