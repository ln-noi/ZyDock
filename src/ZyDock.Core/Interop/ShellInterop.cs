using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZyDock.Core.Interop;

/// <summary>
/// Extracts high resolution shell icons (applications, folders and shell namespaces)
/// through IShellItemImageFactory, and loads user supplied icon files.
/// Packaged apps are handled by <see cref="Services.PackagedIconResolver"/> instead,
/// because the shell only returns a placeholder glyph for AppUserModelIds.
/// </summary>
internal static class ShellInterop
{
    private const uint SIIGBF_RESIZETOFIT = 0x00;
    private const uint SIIGBF_BIGGERSIZEOK = 0x01;
    private const uint SIIGBF_ICONONLY = 0x04;

    private static readonly Guid ImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>True when the shell can resolve the path (used to probe shell locations).</summary>
    public static bool ParsesToShellItem(string parsingName)
    {
        var ptr = CreateItem(parsingName);
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        Marshal.Release(ptr);
        return true;
    }

    public static BitmapSource? GetImage(string parsingName, int pixelSize, bool iconOnly = true)
    {
        if (string.IsNullOrWhiteSpace(parsingName) || pixelSize <= 0)
        {
            return null;
        }

        var ptr = CreateItem(parsingName);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var factory = (IShellItemImageFactory)Marshal.GetTypedObjectForIUnknown(ptr, typeof(IShellItemImageFactory));
            var flags = SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK | (iconOnly ? SIIGBF_ICONONLY : 0);
            var hr = factory.GetImage(new SIZE { cx = pixelSize, cy = pixelSize }, flags, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return FromHBitmap(hBitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            Marshal.Release(ptr);
        }
    }

    private static IntPtr CreateItem(string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return IntPtr.Zero;
        }

        try
        {
            var iid = ImageFactoryIid;
            var hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var ptr);
            return hr == 0 ? ptr : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hBitmap)
    {
        if (GetObjectW(hBitmap, Marshal.SizeOf<BITMAP>(), out var bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
        {
            return null;
        }

        var width = bitmap.bmWidth;
        var height = bitmap.bmHeight;
        var stride = width * 4;
        var buffer = new byte[stride * height];

        var hdc = GetDC(IntPtr.Zero);
        try
        {
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            if (GetDIBits(hdc, hBitmap, 0, (uint)height, buffer, ref info, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
        source.Freeze();
        return source;
    }

    /// <summary>Loads a user supplied .png/.ico/.jpg as a frozen bitmap.</summary>
    public static BitmapSource? LoadImageFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    #region native

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IntPtr ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObjectW(IntPtr hObject, int nCount, out BITMAP lpObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    #endregion
}
