using System.Diagnostics;
using ZyDock.Core.Diagnostics;
using ZyDock.Core.Helpers;
using ZyDock.Core.Interop;
using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>Outcome of a shell launch attempt, with a message that is safe to show.</summary>
public readonly record struct LaunchResult(bool Success, string? Error)
{
    public static LaunchResult Ok() => new(true, null);

    public static LaunchResult Fail(string error) => new(false, error);
}

/// <summary>
/// Launches dock entries through the Windows shell. The dock window itself never takes
/// focus, so the launched application receives it naturally.
/// </summary>
public sealed class AppLauncher
{
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);

    public LaunchResult Launch(DockItem item)
    {
        if (item is null)
        {
            return LaunchResult.Fail("无效的 Dock 项目。");
        }

        try
        {
            return item.Kind switch
            {
                LaunchKind.Application => LaunchApplication(item),
                LaunchKind.Folder => LaunchFolder(item),
                LaunchKind.ShellUri => LaunchShellUri(item),
                LaunchKind.UwpApp => LaunchPackaged(item),
                LaunchKind.Url => LaunchUrl(item),
                _ => LaunchResult.Fail("未知的启动类型。"),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"启动失败：{item.Name} ({item.Target})", ex);
            return LaunchResult.Fail($"无法启动「{item.Name}」：{ex.Message}");
        }
    }

    private static LaunchResult LaunchApplication(DockItem item)
    {
        var target = PathResolver.Expand(item.Target).Trim();
        if (target.Length == 0)
        {
            return LaunchResult.Fail($"「{item.Name}」尚未设置应用路径。");
        }

        var isPath = target.Contains('\\') || target.Contains('/') || target.Contains(':');
        if (isPath && !File.Exists(target))
        {
            return LaunchResult.Fail($"找不到应用：「{item.Name}」\n{target}");
        }

        if (!isPath && !PathResolver.ExistsOnPath(target))
        {
            return LaunchResult.Fail($"系统中找不到「{item.Name}」({target})。");
        }

        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Arguments = item.Arguments ?? string.Empty,
        };

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            startInfo.WorkingDirectory = directory;
        }

        using var process = Process.Start(startInfo);
        Log.Info($"已启动应用：{item.Name} -> {target}");
        return LaunchResult.Ok();
    }

    private static LaunchResult LaunchFolder(DockItem item)
    {
        var target = PathResolver.Expand(item.Target).Trim();
        if (!Directory.Exists(target))
        {
            return LaunchResult.Fail($"文件夹不存在：「{item.Name}」\n{target}");
        }

        using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        Log.Info($"已打开文件夹：{target}");
        return LaunchResult.Ok();
    }

    private static LaunchResult LaunchShellUri(DockItem item)
    {
        var target = (item.Target ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            return LaunchResult.Fail($"「{item.Name}」尚未设置 Shell 位置。");
        }

        // Verify first: an unresolvable shell path makes Windows pop up its own cryptic
        // "application could not start" dialog instead of ours.
        if (!ShellInterop.ParsesToShellItem(target))
        {
            return LaunchResult.Fail($"无法打开「{item.Name}」：系统不认识该位置\n{target}");
        }

        using var process = Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        Log.Info($"已打开 Shell 位置：{target}");
        return LaunchResult.Ok();
    }

    private static LaunchResult LaunchPackaged(DockItem item)
    {
        var aumid = (item.Target ?? string.Empty).Trim();
        if (aumid.Length == 0)
        {
            return LaunchResult.Fail($"「{item.Name}」缺少应用标识。");
        }

        // Preferred: the documented activation API. It returns a real HRESULT, so a refusal
        // becomes a readable message instead of a silent no-op.
        var hr = ApplicationActivation.Activate(aumid, out var processId);
        if (hr == 0)
        {
            Log.Info($"已激活打包应用：{item.Name} -> {aumid}（PID {processId}）");
            return LaunchResult.Ok();
        }

        // Access denied means this process holds a restricted/sandboxed token. Spawning
        // explorer.exe from such a process makes Windows pop its own "0xc0000142 application
        // could not start" dialog, so report the real reason instead of trying.
        if (hr == E_ACCESSDENIED)
        {
            Log.Warn($"打包应用激活被拒绝（受限权限）：{aumid}");
            return LaunchResult.Fail(
                $"无法启动「{item.Name}」：当前进程权限受限（0x{hr:X8}）。\n" +
                "请把 ZyDock 安装到用户目录（例如 C:\\Users\\<用户名>\\Apps\\ZyDock）后重启程序。");
        }

        Log.Warn($"ActivateApplication 失败，HRESULT=0x{hr:X8}，改为通过 explorer 激活：{aumid}");

        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{aumid}")
            {
                UseShellExecute = true,
            });

            Log.Info($"已通过 explorer 激活打包应用：{item.Name} -> {aumid}");
            return LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error($"打包应用激活失败：{aumid}", ex);
            return LaunchResult.Fail($"无法启动「{item.Name}」（系统返回 0x{hr:X8}）");
        }
    }

    private static LaunchResult LaunchUrl(DockItem item)
    {
        var target = (item.Target ?? string.Empty).Trim();
        if (!Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return LaunchResult.Fail($"「{item.Name}」的网址无效。");
        }

        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return LaunchResult.Ok();
    }

    /// <summary>True when the entry still resolves to something launchable.</summary>
    public bool IsReachable(DockItem item) => item.Kind switch
    {
        LaunchKind.Application => item.IsAvailable(),
        LaunchKind.Folder => Directory.Exists(PathResolver.Expand(item.Target)),
        LaunchKind.ShellUri => ShellInterop.ParsesToShellItem(item.Target),
        LaunchKind.UwpApp => ShellInterop.ParsesToShellItem("shell:AppsFolder\\" + item.Target),
        LaunchKind.Url => Uri.TryCreate(item.Target, UriKind.Absolute, out _),
        _ => false,
    };
}
