using Microsoft.Win32;
using ZyDock.Core.Diagnostics;

namespace ZyDock.Core.Services;

/// <summary>
/// Start-with-Windows support. Only the per-user Run key is touched, and only when the
/// user turns the option on; nothing else in the registry is read or written.
/// </summary>
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZyDock";

    private readonly string _executablePath;
    private readonly string _arguments;

    public AutoStartService(string? executablePath = null, string? arguments = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath ?? "ZyDock.exe";
        _arguments = arguments ?? "--background";
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Log.Warn("读取开机启动项失败。", ex);
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{_executablePath}\" {_arguments}".Trim(), RegistryValueKind.String);
                Log.Info($"已启用开机启动：{_executablePath}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("已关闭开机启动。");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("写入开机启动项失败。", ex);
            return false;
        }
    }
}
