using System.Runtime.InteropServices;

namespace ZyDock.App.Interop;

/// <summary>
/// Measures the *real* taskbar window instead of trusting the monitor work area.
///
/// Windows reports a full screen work area as soon as the taskbar auto-hide bit is set, even
/// when Explorer never actually hides the bar (a known quirk, reproducible with
/// SHAppBarMessage). Measuring Shell_TrayWnd keeps the dock above the taskbar either way.
/// </summary>
internal static class TaskbarProbe
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    /// <summary>Height in physical pixels that the visible taskbar occupies at the screen bottom.</summary>
    public static int GetBottomInsetPx(ScreenInfo screen)
    {
        try
        {
            var handle = NativeMethods.FindWindowW("Shell_TrayWnd", null);
            if (handle == IntPtr.Zero || !NativeMethods.IsWindowVisible(handle))
            {
                return 0;
            }

            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                return 0;
            }

            // Only a bottom docked, visible taskbar matters.
            if (rect.Bottom < screen.Bottom - 4 || rect.Bottom > screen.Bottom + 4)
            {
                return 0;
            }

            var height = rect.Height;
            if (height <= 0 || height > screen.Height / 3)
            {
                return 0;
            }

            return Math.Max(0, screen.Bottom - rect.Top);
        }
        catch
        {
            return 0;
        }
    }
}
