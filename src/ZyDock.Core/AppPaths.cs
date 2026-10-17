namespace ZyDock.Core;

/// <summary>
/// Central place for the folders the application writes to.
///
/// The ZYDOCK_DATA_DIR environment variable overrides everything (portable installs and
/// automated tests). Otherwise the standard roaming folder is used, with fallbacks so a
/// locked down profile can never silently lose the user's settings.
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "ZYDOCK_DATA_DIR";

    private static string? _data;
    private static string? _logs;

    private static string? OverrideDirectory
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
        }
    }

    /// <summary>Folder that holds settings.json.</summary>
    public static string DataDirectory => _data ??= ResolveDataDirectory();

    /// <summary>Folder that holds the rolling log file.</summary>
    public static string LogDirectory => _logs ??= ResolveLogDirectory();

    private static string ResolveDataDirectory()
    {
        if (OverrideDirectory is { } custom)
        {
            return custom;
        }

        var candidates = new List<string>(3);
        var roaming = SafeFolder(Environment.SpecialFolder.ApplicationData);
        if (roaming is not null)
        {
            candidates.Add(Path.Combine(roaming, "ZyDock"));
        }

        var profile = SafeFolder(Environment.SpecialFolder.UserProfile);
        if (profile is not null)
        {
            candidates.Add(Path.Combine(profile, "ZyDock"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        foreach (var candidate in candidates)
        {
            if (CanWrite(candidate))
            {
                return candidate;
            }
        }

        // Nothing is writable: still return the preferred path so the error is explicit.
        return candidates[0];
    }

    private static string ResolveLogDirectory()
    {
        if (OverrideDirectory is { } custom)
        {
            return Path.Combine(custom, "logs");
        }

        var local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
        if (local is not null)
        {
            var candidate = Path.Combine(local, "ZyDock", "logs");
            if (CanWrite(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(DataDirectory, "logs");
    }

    private static string? SafeFolder(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
