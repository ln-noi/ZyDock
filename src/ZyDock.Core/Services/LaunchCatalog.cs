using ZyDock.Core.Helpers;
using ZyDock.Core.Interop;
using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>
/// Builds the first-run dock: a small curated set of everyday entry points that are
/// verified to exist on this machine. Missing applications are skipped, and the whole
/// catalog is probe-based so a dock never ships with dead shortcuts.
/// </summary>
public static class LaunchCatalog
{
    public const string SettingsAumid = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
    public const string CalculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
    public const string MediaPlayerAumid = "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic";
    public const string TerminalAumid = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    public const string PhotosAumid = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App";
    public const string StoreAumid = "Microsoft.WindowsStore_8wekyb3d8bbwe!App";

    public static List<DockItem> CreateDefaultItems()
    {
        var items = new List<DockItem>();

        items.Add(new DockItem
        {
            Name = "文件资源管理器",
            Target = "explorer.exe",
            Kind = LaunchKind.Application,
            ProcessName = "explorer",
        });

        AddBrowser(items);
        AddEditor(items);
        AddTerminal(items);
        AddPackaged(items, "设置", SettingsAumid, "SystemSettings");
        items.Add(new DockItem
        {
            Name = "记事本",
            Target = "notepad.exe",
            Kind = LaunchKind.Application,
            ProcessName = "notepad",
        });
        AddPackaged(items, "计算器", CalculatorAumid, "CalculatorApp;Calculator");
        AddMusic(items);

        var downloads = PathResolver.FirstExistingDirectory(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        if (downloads is not null)
        {
            items.Add(new DockItem
            {
                Name = "下载",
                Target = downloads,
                Kind = LaunchKind.Folder,
                ProcessName = "explorer",
            });
        }

        items.Add(new DockItem
        {
            Name = "回收站",
            Target = "shell:RecycleBinFolder",
            Kind = LaunchKind.ShellUri,
            ProcessName = "explorer",
        });

        return items;
    }

    /// <summary>Adds the best available browser found on this machine.</summary>
    private static void AddBrowser(List<DockItem> items)
    {
        var edge = PathResolver.FirstExistingFile(
            @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
            @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
            @"%LocalAppData%\Microsoft\Edge\Application\msedge.exe");

        var chrome = PathResolver.FirstExistingFile(
            @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
            @"%LocalAppData%\Google\Chrome\Application\chrome.exe");

        var firefox = PathResolver.FirstExistingFile(
            @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
            @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe");

        if (edge is not null)
        {
            items.Add(FromExecutable(edge, "Microsoft Edge", "msedge"));
        }
        else if (chrome is not null)
        {
            items.Add(FromExecutable(chrome, "Google Chrome", "chrome"));
        }
        else if (firefox is not null)
        {
            items.Add(FromExecutable(firefox, "Firefox", "firefox"));
        }
    }

    private static void AddEditor(List<DockItem> items)
    {
        var code = PathResolver.FirstExistingFile(
            @"%LocalAppData%\Programs\Microsoft VS Code\Code.exe",
            @"%ProgramFiles%\Microsoft VS Code\Code.exe",
            @"%ProgramFiles(x86)%\Microsoft VS Code\Code.exe");

        if (code is not null)
        {
            items.Add(FromExecutable(code, "Visual Studio Code", "Code"));
            return;
        }

        // Fall back to a shell-provided editor when VS Code is not installed.
        if (ShellInterop.ParsesToShellItem("shell:AppsFolder\\Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"))
        {
            AddPackaged(items, "Windows 记事本", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Notepad");
        }
    }

    private static void AddTerminal(List<DockItem> items)
    {
        if (ShellInterop.ParsesToShellItem("shell:AppsFolder\\" + TerminalAumid))
        {
            AddPackaged(items, "Windows 终端", TerminalAumid, "WindowsTerminal;wt");
            return;
        }

        items.Add(new DockItem
        {
            Name = "命令提示符",
            Target = "cmd.exe",
            Kind = LaunchKind.Application,
            ProcessName = "cmd",
        });
    }

    private static void AddMusic(List<DockItem> items)
    {
        if (ShellInterop.ParsesToShellItem("shell:AppsFolder\\" + MediaPlayerAumid))
        {
            AddPackaged(items, "媒体播放器", MediaPlayerAumid, "MediaPlayerApp;Music.UI");
            return;
        }

        var spotify = PathResolver.FirstExistingFile(@"%AppData%\Spotify\Spotify.exe");
        if (spotify is not null)
        {
            items.Add(FromExecutable(spotify, "Spotify", "Spotify"));
        }
    }

    private static void AddPackaged(List<DockItem> items, string name, string aumid, string? processName)
    {
        // Only add packaged apps that are actually registered for this user.
        if (!ShellInterop.ParsesToShellItem("shell:AppsFolder\\" + aumid))
        {
            return;
        }

        items.Add(new DockItem
        {
            Name = name,
            Target = aumid,
            Kind = LaunchKind.UwpApp,
            ProcessName = processName,
        });
    }

    public static DockItem FromExecutable(string path, string? name = null, string? processName = null)
    {
        return new DockItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
            Target = path,
            Kind = LaunchKind.Application,
            ProcessName = processName,
        };
    }
}
