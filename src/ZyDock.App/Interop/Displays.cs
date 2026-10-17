namespace ZyDock.App.Interop;

/// <summary>One physical display, in physical pixels.</summary>
internal sealed record ScreenInfo(string DeviceName, int Left, int Top, int Width, int Height, int WorkLeft, int WorkTop, int WorkWidth, int WorkHeight, bool IsPrimary, double Scale)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public override string ToString() => IsPrimary ? $"{DeviceName}（主屏幕 {Width}×{Height}）" : $"{DeviceName}（{Width}×{Height}）";
}

/// <summary>Monitor enumeration with per-monitor DPI, without depending on WinForms.</summary>
internal static class Displays
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public static IReadOnlyList<ScreenInfo> All()
    {
        var list = new List<ScreenInfo>();
        var primary = GetPrimaryBounds();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var scale = 1.0;
            if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }

            var isPrimary = (info.dwFlags & 1) != 0;
            list.Add(new ScreenInfo(
                info.szDevice ?? "DISPLAY",
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Width,
                info.rcMonitor.Height,
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Width,
                info.rcWork.Height,
                isPrimary,
                scale));

            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            // Extremely defensive fallback: a single screen from the primary metrics.
            var width = GetSystemMetrics(SM_CXSCREEN);
            var height = GetSystemMetrics(SM_CYSCREEN);
            list.Add(new ScreenInfo("DISPLAY", 0, 0, width, height, 0, 0, width, height, true, 1.0));
        }

        return list.OrderBy(s => s.IsPrimary ? 0 : 1).ThenBy(s => s.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ScreenInfo Resolve(IReadOnlyList<ScreenInfo> screens, int screenIndex)
    {
        if (screens.Count == 0)
        {
            throw new InvalidOperationException("没有可用的显示器。");
        }

        if (screenIndex >= 0 && screenIndex < screens.Count)
        {
            return screens[screenIndex];
        }

        return screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
    }

    public static ScreenInfo FromPoint(int x, int y, IReadOnlyList<ScreenInfo> screens)
    {
        foreach (var screen in screens)
        {
            if (x >= screen.Left && x < screen.Right && y >= screen.Top && y < screen.Bottom)
            {
                return screen;
            }
        }

        return Resolve(screens, -1);
    }

    /// <summary>Current primary display refresh rate in Hz (0 when unknown).</summary>
    public static int GetRefreshRateHz()
    {
        try
        {
            var mode = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettingsW(null, -1 /* ENUM_CURRENT_SETTINGS */, ref mode))
            {
                return (int)mode.dmDisplayFrequency;
            }
        }
        catch
        {
        }

        return 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct DEVMODE
    {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    private static (int, int) GetPrimaryBounds() => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
