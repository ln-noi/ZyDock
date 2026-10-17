using System.Runtime.InteropServices;
using ZyDock.Core.Diagnostics;

namespace ZyDock.App.Interop;

/// <summary>
/// Reversible Windows taskbar auto-hide driven by the documented AppBar API
/// (no registry edits, no shell replacement). The previous state is remembered so
/// the taskbar is restored when the dock exits.
/// </summary>
internal static class TaskbarAutoHide
{
    private static int? _originalState;

    public static bool IsAutoHideOn() => (ReadState() & NativeMethods.ABS_AUTOHIDE) != 0;

    private static int ReadState()
    {
        // A fresh struct per call: ABM_GETSTATE may fill in hWnd, and reusing that struct
        // for ABM_SETSTATE makes the shell reject the change.
        var data = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        return (int)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    /// <summary>Requests auto-hide (or always-on-top) and returns true when the call succeeded.</summary>
    public static bool SetAutoHide(bool autoHide)
    {
        try
        {
            if (_originalState is null)
            {
                _originalState = ReadState();
            }

            var target = autoHide
                ? NativeMethods.ABS_AUTOHIDE
                : NativeMethods.ABS_ALWAYSONTOP;

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                lParam = new IntPtr(target),
            };

            var result = NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);

            // ABM_SETSTATE answers TRUE when the state was accepted.
            var ok = result != IntPtr.Zero;
            var confirmed = ReadState();
            Log.Info($"任务栏自动隐藏 -> {(autoHide ? "开" : "关")}，返回={(long)result}，回读状态={confirmed}");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error("设置任务栏自动隐藏失败。", ex);
            return false;
        }
    }

    /// <summary>Restores whatever the taskbar state was before the dock touched it.</summary>
    public static void Restore()
    {
        try
        {
            if (_originalState is not { } original)
            {
                return;
            }

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                lParam = new IntPtr(original),
            };

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
            _originalState = null;
            Log.Info("已恢复任务栏原始状态。");
        }
        catch (Exception ex)
        {
            Log.Warn("恢复任务栏状态失败。", ex);
        }
    }
}
