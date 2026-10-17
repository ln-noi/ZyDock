using System.Runtime.InteropServices;

namespace ZyDock.Core.Interop;

/// <summary>
/// Official activation path for packaged (UWP/MSIX) applications.
///
/// The commonly copied "explorer.exe shell:AppsFolder\&lt;AUMID&gt;" trick silently fails (or pops
/// a Windows error dialog) depending on the shell state; IApplicationActivationManager is the
/// documented API and reports a real HRESULT when activation is refused.
/// </summary>
internal static class ApplicationActivation
{
    private const uint AO_NONE = 0;

    private static readonly Guid ClsidApplicationActivationManager = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    /// <summary>Activates the packaged app. Returns the HRESULT (0 = success).</summary>
    public static int Activate(string appUserModelId, out uint processId)
    {
        processId = 0;
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        object? managerObject = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidApplicationActivationManager, throwOnError: true);
            managerObject = Activator.CreateInstance(type!);
            if (managerObject is not IApplicationActivationManager manager)
            {
                return unchecked((int)0x80004002); // E_NOINTERFACE
            }

            return manager.ActivateApplication(appUserModelId, null, AO_NONE, out processId);
        }
        catch (Exception ex)
        {
            return ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
        }
        finally
        {
            if (managerObject is not null && Marshal.IsComObject(managerObject))
            {
                Marshal.ReleaseComObject(managerObject);
            }
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }
}
