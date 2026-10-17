using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>Turns a file / folder / URL picked in the settings window into a dock entry.</summary>
public static class DockItemFactory
{
    public static DockItem? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        if (Directory.Exists(expanded))
        {
            return new DockItem
            {
                Name = new DirectoryInfo(expanded).Name,
                Target = expanded,
                Kind = LaunchKind.Folder,
                ProcessName = "explorer",
            };
        }

        if (!File.Exists(expanded))
        {
            return null;
        }

        var extension = Path.GetExtension(expanded).ToLowerInvariant();
        if (extension == ".url")
        {
            return new DockItem
            {
                Name = Path.GetFileNameWithoutExtension(expanded),
                Target = expanded,
                Kind = LaunchKind.ShellUri,
            };
        }

        return new DockItem
        {
            Name = Path.GetFileNameWithoutExtension(expanded),
            Target = expanded,
            Kind = LaunchKind.Application,
            ProcessName = extension is ".exe" or ".com" ? Path.GetFileNameWithoutExtension(expanded) : null,
        };
    }

    public static DockItem FromShellUri(string shellPath, string? name = null)
    {
        return new DockItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? shellPath : name,
            Target = shellPath,
            Kind = LaunchKind.ShellUri,
            ProcessName = "explorer",
        };
    }

    public static DockItem FromPackagedApp(string aumid, string? name = null)
    {
        return new DockItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? aumid : name,
            Target = aumid,
            Kind = LaunchKind.UwpApp,
        };
    }
}
