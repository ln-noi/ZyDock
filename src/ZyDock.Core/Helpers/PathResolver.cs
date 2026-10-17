namespace ZyDock.Core.Helpers;

/// <summary>Small PATH / environment helpers used by the launcher and the catalog.</summary>
public static class PathResolver
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd", ".lnk"];

    public static bool ExistsOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDir in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim().Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                if (File.Exists(System.IO.Path.Combine(dir, fileName)))
                {
                    return true;
                }

                if (!System.IO.Path.HasExtension(fileName))
                {
                    foreach (var ext in Extensions)
                    {
                        if (File.Exists(System.IO.Path.Combine(dir, fileName + ext)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry - ignore it.
            }
        }

        return false;
    }

    public static string Expand(string? value) => Environment.ExpandEnvironmentVariables(value ?? string.Empty);

    /// <summary>
    /// Turns a bare executable name ("explorer.exe") into a full path so the shell can
    /// resolve a real icon for it. Returns null when it cannot be found on this machine.
    /// </summary>
    public static string? ResolveExecutable(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (expanded.Length == 0)
        {
            return null;
        }

        if (expanded.Contains('\\') || expanded.Contains('/') || expanded.Contains(':'))
        {
            return File.Exists(expanded) ? expanded : null;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            System.IO.Path.Combine(windows, expanded),
            System.IO.Path.Combine(windows, "System32", expanded),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDir in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim().Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = System.IO.Path.Combine(dir, expanded);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    /// <summary>Returns the first existing file out of the candidates.</summary>
    public static string? FirstExistingFile(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate);
                if (File.Exists(expanded))
                {
                    return expanded;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    public static string? FirstExistingDirectory(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate);
                if (Directory.Exists(expanded))
                {
                    return expanded;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
