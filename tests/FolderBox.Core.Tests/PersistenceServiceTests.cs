using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Xunit;

namespace FolderBox.Core.Tests;

public class PersistenceServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FolderBoxTests", Guid.NewGuid().ToString("N"));

    public PersistenceServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Folders_round_trip()
    {
        var p = new PersistenceService(_dir);
        var doc = new FoldersDocument();
        doc.Folders.Add(new FolderWidget { DisplayName = "A", FolderPath = @"C:\A", X = 10, Y = 20, MonitorId = @"\\.\DISPLAY1", IsLocked = true });
        p.SaveFolders(doc);

        var loaded = new PersistenceService(_dir).LoadFolders();
        Assert.Single(loaded.Folders);
        Assert.Equal("A", loaded.Folders[0].DisplayName);
        Assert.Equal(@"C:\A", loaded.Folders[0].FolderPath);
        Assert.True(loaded.Folders[0].IsLocked);
        Assert.Equal(FoldersDocument.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Save_is_atomic_and_keeps_a_backup()
    {
        var p = new PersistenceService(_dir);
        p.SaveFolders(new FoldersDocument { Folders = { new FolderWidget { DisplayName = "v1", FolderPath = @"C:\1" } } });
        p.SaveFolders(new FoldersDocument { Folders = { new FolderWidget { DisplayName = "v2", FolderPath = @"C:\2" } } });

        Assert.True(File.Exists(p.FoldersPath));
        Assert.True(File.Exists(p.FoldersPath + ".bak"));
        Assert.False(File.Exists(p.FoldersPath + ".tmp"));
        Assert.Contains("v2", File.ReadAllText(p.FoldersPath));
        Assert.Contains("v1", File.ReadAllText(p.FoldersPath + ".bak"));
    }

    [Fact]
    public void Corrupt_file_falls_back_to_backup()
    {
        var p = new PersistenceService(_dir);
        p.SaveFolders(new FoldersDocument { Folders = { new FolderWidget { DisplayName = "good", FolderPath = @"C:\g" } } });
        p.SaveFolders(new FoldersDocument { Folders = { new FolderWidget { DisplayName = "good2", FolderPath = @"C:\g2" } } });
        File.WriteAllText(p.FoldersPath, "{ this is not json");

        var loaded = new PersistenceService(_dir).LoadFolders();
        Assert.Single(loaded.Folders);
        Assert.Equal("good", loaded.Folders[0].DisplayName);
        Assert.True(File.Exists(p.FoldersPath + ".corrupt"));
    }

    [Fact]
    public void Corrupt_file_without_backup_yields_empty_document_not_exception()
    {
        var p = new PersistenceService(_dir);
        File.WriteAllText(p.FoldersPath, "\0\0\0garbage");
        var loaded = p.LoadFolders();
        Assert.Empty(loaded.Folders);
    }

    [Fact]
    public void Settings_are_normalised_on_load()
    {
        var p = new PersistenceService(_dir);
        File.WriteAllText(p.SettingsPath, """{ "version": 1, "settings": { "gridWidth": 5, "panelWidth": 9999, "theme": "Dark" } }""");
        var s = p.LoadSettings();
        Assert.Equal(80, s.GridWidth);
        Assert.Equal(720, s.PanelWidth);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.True(s.AlignToGrid); // default preserved
    }

    [Fact]
    public void Entries_without_path_are_dropped_and_ids_are_filled()
    {
        var p = new PersistenceService(_dir);
        File.WriteAllText(p.FoldersPath, """{ "version": 1, "folders": [ { "displayName": "x", "folderPath": "" }, { "folderPath": "C:\\ok", "id": "" } ] }""");
        var loaded = p.LoadFolders();
        Assert.Single(loaded.Folders);
        Assert.False(string.IsNullOrEmpty(loaded.Folders[0].Id));
        Assert.Equal("ok", loaded.Folders[0].DisplayName);
    }
}
