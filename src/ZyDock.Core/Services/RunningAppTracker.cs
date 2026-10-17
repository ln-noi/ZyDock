using System.Diagnostics;
using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>
/// Cheap "is this app running" probe used for the dock indicator dots.
/// The dock calls <see cref="Refresh"/> on a slow timer; the call is a handful of
/// Process.GetProcessesByName lookups (no full process enumeration).
/// </summary>
public sealed class RunningAppTracker
{
    private readonly Dictionary<string, bool> _nameCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, bool> Refresh(IReadOnlyList<DockItem> items)
    {
        var result = new Dictionary<string, bool>(items.Count, StringComparer.Ordinal);

        foreach (var item in items)
        {
            var names = GetProcessNames(item);
            var running = false;
            foreach (var name in names)
            {
                if (IsRunningCached(name))
                {
                    running = true;
                    break;
                }
            }

            result[item.Id] = running;
        }

        return result;
    }

    public static string[] GetProcessNames(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProcessName))
        {
            return item.ProcessName.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return item.Kind switch
        {
            LaunchKind.Application => DeriveFromTarget(item.Target),
            LaunchKind.Folder or LaunchKind.ShellUri => ["explorer"],
            _ => [],
        };

        static string[] DeriveFromTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return [];
            }

            try
            {
                var name = Path.GetFileNameWithoutExtension(Environment.ExpandEnvironmentVariables(target));
                return string.IsNullOrWhiteSpace(name) ? [] : [name];
            }
            catch (ArgumentException)
            {
                return [];
            }
        }
    }

    private bool IsRunningCached(string processName)
    {
        if (_nameCache.TryGetValue(processName, out var cached))
        {
            return cached;
        }

        bool running;
        try
        {
            running = Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            running = false;
        }

        _nameCache[processName] = running;
        return running;
    }
}
