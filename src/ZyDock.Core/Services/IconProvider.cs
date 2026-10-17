using System.Windows.Media;
using ZyDock.Core.Helpers;
using ZyDock.Core.Interop;
using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>
/// Produces the icon for a dock entry: custom image first, then the real shell icon
/// (256 px source so magnification stays sharp), then a vector fallback.
/// Every result is frozen and cached, so the dock never creates bitmaps per frame.
/// </summary>
public sealed class IconProvider
{
    private const int SourcePixels = 256;

    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource GetIcon(DockItem item)
    {
        var key = BuildKey(item);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var icon = Resolve(item);
        lock (_cache)
        {
            _cache[key] = icon;
        }

        return icon;
    }

    public void Invalidate(DockItem item)
    {
        lock (_cache)
        {
            _cache.Remove(BuildKey(item));
        }
    }

    public void Clear()
    {
        lock (_cache)
        {
            _cache.Clear();
        }
    }

    private static string BuildKey(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconOverridePath))
        {
            return "file:" + item.IconOverridePath;
        }

        return item.Kind + "|" + item.Target + "|" + item.Name;
    }

    private static ImageSource Resolve(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconOverridePath))
        {
            var custom = ShellInterop.LoadImageFile(Environment.ExpandEnvironmentVariables(item.IconOverridePath));
            if (custom is not null)
            {
                return custom;
            }
        }

        // Packaged apps: read the real logo straight out of the package. The shell item
        // APIs only hand back the generic document glyph for AppUserModelIds.
        if (item.Kind == LaunchKind.UwpApp)
        {
            var packaged = PackagedIconResolver.Load(item.Target);
            if (packaged is not null)
            {
                return packaged;
            }
        }

        var parsingName = ToParsingName(item);
        if (parsingName is not null)
        {
            var image = ShellInterop.GetImage(parsingName, SourcePixels);
            if (image is not null)
            {
                return image;
            }
        }

        return FallbackIcons.ForName(item.Name, item.Kind == LaunchKind.Folder);
    }

    /// <summary>Maps a dock entry onto something the shell can resolve to an image.</summary>
    private static string? ToParsingName(DockItem item)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            return null;
        }

        return item.Kind switch
        {
            // Bare names such as "explorer.exe" must be resolved to a full path first,
            // otherwise the shell answers with the "unknown file" placeholder icon.
            LaunchKind.Application => PathResolver.ResolveExecutable(target) ?? target,
            LaunchKind.Folder => target,
            LaunchKind.ShellUri => target,
            LaunchKind.UwpApp => "shell:AppsFolder\\" + target,
            LaunchKind.Url => null,
            _ => target,
        };
    }
}
