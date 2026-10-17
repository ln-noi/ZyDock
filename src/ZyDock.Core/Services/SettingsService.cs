using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZyDock.Core.Diagnostics;
using ZyDock.Core.Models;

namespace ZyDock.Core.Services;

/// <summary>
/// Loads and saves <see cref="DockSettings"/> under %APPDATA%\ZyDock.
/// Writes are atomic (temp file + replace) and a damaged file is preserved as a
/// timestamped backup instead of being silently deleted.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly object _gate = new();
    private DockSettings _current = new();

    public SettingsService()
    {
        Directory = AppPaths.DataDirectory;
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public string Directory { get; }

    public string FilePath { get; set; }

    public DockSettings Current => _current;

    /// <summary>Human readable note about the last load problem (shown in the settings window).</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>True when the file did not exist and defaults were generated.</summary>
    public bool IsFirstRun { get; private set; }

    public DockSettings Load()
    {
        lock (_gate)
        {
            LoadWarning = null;
            try
            {
                if (!File.Exists(FilePath) && TryMigrateLegacySettings())
                {
                    Log.Info($"已从旧版本目录导入配置：{FilePath}");
                }

                if (!File.Exists(FilePath))
                {
                    IsFirstRun = true;
                    _current = new DockSettings { Items = LaunchCatalog.CreateDefaultItems() }.Clamp();
                    return _current;
                }

                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<DockSettings>(json, JsonOptions);

                if (loaded is null)
                {
                    throw new InvalidDataException("settings.json 内容为空");
                }

                if (loaded.Items is null || loaded.Items.Count == 0)
                {
                    // A file that lost its item list is treated as a fresh catalog,
                    // but every other preference is kept.
                    loaded.Items = LaunchCatalog.CreateDefaultItems();
                }

                Migrate(loaded);
                _current = loaded.Clamp();
                return _current;
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败，已回退到默认配置。", ex);
                LoadWarning = $"配置文件无法读取（{ex.GetType().Name}），已备份并恢复默认设置。";
                BackupBrokenFile();
                _current = new DockSettings { Items = LaunchCatalog.CreateDefaultItems() }.Clamp();
                IsFirstRun = true;
                return _current;
            }
        }
    }

    public bool Save(DockSettings settings)
    {
        lock (_gate)
        {
            try
            {
                settings.Clamp();
                settings.SchemaVersion = DockSettings.CurrentSchemaVersion;
                System.IO.Directory.CreateDirectory(Directory);

                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));

                if (File.Exists(FilePath))
                {
                    File.Replace(temp, FilePath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, FilePath);
                }

                _current = settings;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败。", ex);
                return false;
            }
        }
    }

    public void ResetToDefaults()
    {
        lock (_gate)
        {
            _current = new DockSettings { Items = LaunchCatalog.CreateDefaultItems() }.Clamp();
        }
    }

    private static void Migrate(DockSettings settings)
    {
        // Reserved for future schema versions; v1 is the first release.
        if (settings.SchemaVersion <= 0)
        {
            settings.SchemaVersion = DockSettings.CurrentSchemaVersion;
        }
    }

    /// <summary>
    /// The application used to be called MacDock. If a settings file from that era exists next
    /// to the current folder, take it over once so the user keeps their dock layout.
    /// </summary>
    private bool TryMigrateLegacySettings()
    {
        try
        {
            var parent = Path.GetDirectoryName(Directory);
            if (string.IsNullOrEmpty(parent))
            {
                return false;
            }

            var legacy = Path.Combine(parent, "MacDock", "settings.json");
            if (!File.Exists(legacy))
            {
                return false;
            }

            System.IO.Directory.CreateDirectory(Directory);
            File.Copy(legacy, FilePath, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("导入旧版本配置失败。", ex);
            return false;
        }
    }

    private void BackupBrokenFile()
    {
        try
        {
            var backup = Path.Combine(Directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, backup, overwrite: true);
            LoadWarning += $" 原文件已保存为 {Path.GetFileName(backup)}。";
        }
        catch (Exception ex)
        {
            Log.Warn("无法备份损坏的配置文件。", ex);
        }
    }
}
