namespace ZyDock.Core.Models;

/// <summary>
/// One entry of the dock. Everything needed to draw the icon, launch the target and
/// light up the running indicator lives here, so the settings file stays portable.
/// </summary>
public sealed class DockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Label shown in the hover tooltip and in the settings list.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Executable path, folder path, <c>shell:</c> path, AppUserModelId or URL,
    /// depending on <see cref="Kind"/>.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    public LaunchKind Kind { get; set; } = LaunchKind.Application;

    /// <summary>Optional command line arguments for <see cref="LaunchKind.Application"/>.</summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Process names used for the "is running" dot. Multiple names may be separated by ';'.
    /// When null the process name is derived from <see cref="Target"/>.
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>Optional custom .ico/.png used instead of the shell icon.</summary>
    public string? IconOverridePath { get; set; }

    /// <summary>True when the entry can be resolved on this machine right now.</summary>
    public bool IsAvailable()
    {
        return Kind switch
        {
            LaunchKind.Folder => !string.IsNullOrWhiteSpace(Target) && System.IO.Directory.Exists(Environment.ExpandEnvironmentVariables(Target)),
            LaunchKind.Application => !string.IsNullOrWhiteSpace(Target) && ServiceLocatorHasExecutable(Target),
            LaunchKind.ShellUri => !string.IsNullOrWhiteSpace(Target),
            LaunchKind.UwpApp => !string.IsNullOrWhiteSpace(Target),
            LaunchKind.Url => !string.IsNullOrWhiteSpace(Target),
            _ => false,
        };
    }

    private static bool ServiceLocatorHasExecutable(string target)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target);
        if (System.IO.File.Exists(expanded))
        {
            return true;
        }

        // Bare file names (notepad.exe) are resolved through PATH.
        if (expanded.Contains('\\') || expanded.Contains('/'))
        {
            return false;
        }

        return Helpers.PathResolver.ExistsOnPath(expanded);
    }

    public DockItem Clone()
    {
        return new DockItem
        {
            Id = Id,
            Name = Name,
            Target = Target,
            Kind = Kind,
            Arguments = Arguments,
            ProcessName = ProcessName,
            IconOverridePath = IconOverridePath,
        };
    }
}
