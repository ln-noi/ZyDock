using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using Microsoft.Win32;
using ZyDock.Core.Diagnostics;

namespace ZyDock.Core.Services;

/// <summary>
/// Resolves the real 256 px logo of a packaged (UWP/MSIX) application.
///
/// The shell exposes no high resolution icon for AppUserModelIds (the item image factory
/// answers with the generic document glyph), so the logo is read from the package itself:
/// AUMID -> package root (HKCU AppModel repository) -> AppxManifest.xml -> the unplated
/// targetsize-256 asset that Windows uses on the Start menu.
/// </summary>
public static class PackagedIconResolver
{
    private const string RepositoryKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly string[] Qualifiers =
    [
        ".targetsize-256_altform-unplated",
        ".targetsize-256",
        ".targetsize-192_altform-unplated",
        ".targetsize-192",
        ".targetsize-96",
        ".scale-400_altform-unplated",
        ".scale-400",
        ".scale-200_altform-unplated",
        ".scale-200",
        ".scale-150",
        ".targetsize-48_altform-unplated",
        ".scale-125",
        ".scale-100",
    ];

    private static readonly Dictionary<string, string?> PathCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Best logo file for the AUMID, or null when it cannot be located.</summary>
    public static string? FindLogoFile(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        lock (PathCache)
        {
            if (PathCache.TryGetValue(aumid, out var cached))
            {
                return cached;
            }
        }

        var resolved = Resolve(aumid);
        lock (PathCache)
        {
            PathCache[aumid] = resolved;
        }

        return resolved;
    }

    public static ImageSource? Load(string aumid)
    {
        var path = FindLogoFile(aumid);
        if (path is null)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"无法加载打包应用图标：{path}", ex);
            return null;
        }
    }

    private static string? Resolve(string aumid)
    {
        try
        {
            var bang = aumid.IndexOf('!');
            if (bang <= 0 || bang >= aumid.Length - 1)
            {
                return null;
            }

            var familyName = aumid[..bang];
            var appId = aumid[(bang + 1)..];

            var root = FindPackageRoot(familyName);
            if (root is null)
            {
                return null;
            }

            var manifest = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifest))
            {
                return null;
            }

            var logoRelative = ReadLogoPath(manifest, appId) ?? @"Images\Square44x44Logo.png";
            var logoDirectory = Path.GetDirectoryName(logoRelative) ?? string.Empty;
            var logoName = Path.GetFileNameWithoutExtension(logoRelative);

            var directory = Path.Combine(root, logoDirectory);
            foreach (var qualifier in Qualifiers)
            {
                var candidate = Path.Combine(directory, logoName + qualifier + ".png");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var plain = Path.Combine(directory, logoName + ".png");
            if (File.Exists(plain))
            {
                return plain;
            }

            // Some packages only ship a Square150x150 tile logo.
            var tileName = logoName.Replace("Square44x44Logo", "Square150x150Logo");
            foreach (var qualifier in Qualifiers)
            {
                var candidate = Path.Combine(directory, tileName + qualifier + ".png");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"解析打包应用图标失败：{aumid}", ex);
            return null;
        }
    }

    private static string? FindPackageRoot(string familyName)
    {
        try
        {
            var separator = familyName.IndexOf('_');
            if (separator <= 0)
            {
                return null;
            }

            var name = familyName[..separator];
            var publisher = familyName[(separator + 1)..];

            using var key = Registry.CurrentUser.OpenSubKey(RepositoryKey);
            if (key is null)
            {
                return null;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) ||
                    !subKeyName.EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey?.GetValue("PackageRootFolder") is string root && Directory.Exists(root))
                {
                    return root;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取打包应用安装位置失败：{familyName}", ex);
        }

        return null;
    }

    private static string? ReadLogoPath(string manifestPath, string appId)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
            };

            using var reader = XmlReader.Create(manifestPath, settings);
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);

            var applications = document.GetElementsByTagName("Application");
            foreach (XmlNode? node in applications)
            {
                if (node is null)
                {
                    continue;
                }

                var id = node.Attributes?["Id"]?.Value;
                if (!string.Equals(id, appId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (XmlNode? child in node.ChildNodes)
                {
                    if (child is null || !string.Equals(child.LocalName, "VisualElements", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var logo = child.Attributes?["Square44x44Logo"]?.Value
                               ?? child.Attributes?["Logo"]?.Value;
                    if (!string.IsNullOrWhiteSpace(logo))
                    {
                        return logo;
                    }
                }
            }

            // Fall back to the first VisualElements logo in the manifest.
            var visuals = document.GetElementsByTagName("uap:VisualElements");
            foreach (XmlNode? node in visuals)
            {
                var logo = node?.Attributes?["Square44x44Logo"]?.Value ?? node?.Attributes?["Logo"]?.Value;
                if (!string.IsNullOrWhiteSpace(logo))
                {
                    return logo;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取 AppxManifest 失败：{manifestPath}", ex);
        }

        return null;
    }
}
