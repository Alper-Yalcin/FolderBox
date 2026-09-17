using System.Text.Json;
using System.Text.Json.Serialization;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;

namespace FolderBox.Core.Services;

/// <summary>
/// Reads/writes versioned JSON documents under %LocalAppData%\FolderBox.
/// Writes are atomic (temp file + replace) and a .bak copy is kept for corruption recovery.
/// </summary>
public sealed class PersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public string DataDirectory { get; }
    public string FoldersPath => Path.Combine(DataDirectory, "folders.json");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string LogDirectory => Path.Combine(DataDirectory, "Logs");

    public PersistenceService(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderBox");
        Directory.CreateDirectory(DataDirectory);
    }

    public FoldersDocument LoadFolders()
    {
        var doc = LoadDocument<FoldersDocument>(FoldersPath) ?? new FoldersDocument();
        doc.Folders ??= new List<FolderWidget>();
        // Defensive normalisation: drop entries that can never be shown.
        doc.Folders.RemoveAll(f => string.IsNullOrWhiteSpace(f.FolderPath));
        foreach (var f in doc.Folders)
        {
            if (string.IsNullOrWhiteSpace(f.Id)) f.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(f.DisplayName))
                f.DisplayName = Utilities.PathHelpers.GetDisplayNameForFolder(f.FolderPath);
        }
        return doc;
    }

    public void SaveFolders(FoldersDocument doc)
    {
        doc.Version = FoldersDocument.CurrentVersion;
        SaveDocument(FoldersPath, doc);
    }

    public AppSettings LoadSettings()
    {
        var doc = LoadDocument<SettingsDocument>(SettingsPath) ?? new SettingsDocument();
        doc.Settings ??= new AppSettings();
        doc.Settings.Normalize();
        return doc.Settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.Normalize();
        SaveDocument(SettingsPath, new SettingsDocument { Settings = settings });
    }

    private T? LoadDocument<T>(string path) where T : class
    {
        lock (_gate)
        {
            var result = TryRead<T>(path);
            if (result is not null) return result;

            if (File.Exists(path))
            {
                // Keep the unreadable file for inspection instead of silently overwriting it.
                try
                {
                    var corrupt = path + ".corrupt";
                    File.Copy(path, corrupt, overwrite: true);
                    Log.Warn($"Unreadable document preserved as {corrupt}");
                }
                catch { }
            }

            var backup = path + ".bak";
            if (File.Exists(backup))
            {
                Log.Warn($"Falling back to backup for {Path.GetFileName(path)}");
                result = TryRead<T>(backup);
                if (result is not null) return result;
            }
            return null;
        }
    }

    private static T? TryRead<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read {path}", ex);
            return null;
        }
    }

    private void SaveDocument<T>(string path, T doc)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(doc, JsonOptions);
            var tmp = path + ".tmp";
            var bak = path + ".bak";
            try
            {
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                {
                    // Atomic on NTFS: the destination is replaced and the old file becomes the backup.
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save {path}", ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }
    }
}
