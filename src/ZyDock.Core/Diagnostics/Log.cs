using System.Text;
using System.Text.RegularExpressions;

namespace ZyDock.Core.Diagnostics;

/// <summary>
/// Tiny append-only log. Never throws: a logging failure must not take the app down.
/// Rotates at 2 MB, keeps one previous file.
///
/// Every line is **redacted** before it is written: absolute paths under the current user
/// profile become %USERPROFILE%, and any other drive-letter path under a user profile becomes
/// %USERPROFILE% as well. That keeps diagnostics useful (relative/app paths stay readable)
/// without leaving a plain-text map of the machine's usernames behind.
/// </summary>
public static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();

    private static readonly Regex UserProfilePath = new(
        @"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProfilesRoot = new(
        @"[A-Za-z]:\\Users\\",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string ProfilePrefix =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');

    private static readonly string[] SensitivePrefixes = BuildSensitivePrefixes();

    public static string Directory { get; } = AppPaths.LogDirectory;

    public static string FilePath => Path.Combine(Directory, "app.log");

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>Removes the account name from absolute paths. Safe to call on any string.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;

        foreach (var prefix in SensitivePrefixes)
        {
            if (prefix.Length == 0)
            {
                continue;
            }

            result = result.Replace(prefix + "\\", "%USERPROFILE%\\", StringComparison.OrdinalIgnoreCase);
            result = result.Replace(prefix, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        // Any remaining C:\Users\<someone> (another account, diagnostics from an exception).
        try
        {
            result = UserProfilePath.Replace(result, "%USERPROFILE%");
            result = ProfilesRoot.Replace(result, "%USERPROFILE%\\");
        }
        catch
        {
            // A malformed string must never break logging.
        }

        return result;
    }

    private static string[] BuildSensitivePrefixes()
    {
        var list = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value.TrimEnd('\\'));
            }
        }

        Add(ProfilePrefix);
        Add(Environment.GetEnvironmentVariable("USERPROFILE"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Add(Environment.GetEnvironmentVariable("APPDATA"));
        Add(Environment.GetEnvironmentVariable("LOCALAPPDATA"));

        return list
            .Where(p => p.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ToArray();
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = new StringBuilder(160)
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(Redact(message));

            if (ex is not null)
            {
                line.AppendLine().Append(Redact(ex.ToString()));
            }

            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                RotateIfNeeded();
                File.AppendAllText(FilePath, line.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Intentionally swallowed.
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var backup = Path.Combine(Directory, "app.previous.log");
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(FilePath, backup);
    }
}
