namespace ZyDock.Core.Models;

/// <summary>How a dock entry should be launched.</summary>
public enum LaunchKind
{
    /// <summary>An executable on disk (or a bare name resolved through PATH).</summary>
    Application = 0,

    /// <summary>A folder, opened with File Explorer.</summary>
    Folder = 1,

    /// <summary>A shell namespace path such as <c>shell:Downloads</c> or <c>shell:RecycleBinFolder</c>.</summary>
    ShellUri = 2,

    /// <summary>A packaged (UWP/MSIX) application identified by its AppUserModelId.</summary>
    UwpApp = 3,

    /// <summary>An http(s) address opened by the default browser.</summary>
    Url = 4,
}
