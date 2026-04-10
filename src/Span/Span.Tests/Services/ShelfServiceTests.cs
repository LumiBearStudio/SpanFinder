using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Span.Models;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class ShelfServiceTests
{
    private string _tempDir = string.Empty;
    private IconService _icons = null!;
    private SettingsServiceStub _settings = null!;
    private ShelfService _service = null!;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpanShelfTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _icons = new IconService();
        IconService.Current = _icons;
        _settings = new SettingsServiceStub();
        _service = new ShelfService(_icons, _settings);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string MakeDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── CreateShelfItems ────────────────────────────

    [TestMethod]
    public void CreateShelfItems_AddsExistingFile()
    {
        var f = MakeFile("a.txt", "hello");
        var existing = new ObservableCollection<ShelfItem>();

        var result = _service.CreateShelfItems(new() { f }, existing);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(f, result[0].Path);
        Assert.AreEqual("a.txt", result[0].Name);
        Assert.IsFalse(result[0].IsDirectory);
        Assert.AreEqual(5, result[0].FileSize);
        Assert.AreEqual(_tempDir, result[0].SourceFolder);
    }

    [TestMethod]
    public void CreateShelfItems_AddsExistingDirectory()
    {
        var d = MakeDir("subdir");
        var existing = new ObservableCollection<ShelfItem>();

        var result = _service.CreateShelfItems(new() { d }, existing);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].IsDirectory);
        Assert.AreEqual(0, result[0].FileSize);
    }

    [TestMethod]
    public void CreateShelfItems_SkipsNonExistentPaths()
    {
        var ghost = Path.Combine(_tempDir, "no-such-file.txt");
        var existing = new ObservableCollection<ShelfItem>();

        var result = _service.CreateShelfItems(new() { ghost }, existing);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void CreateShelfItems_SkipsDuplicatesAgainstExisting()
    {
        var f = MakeFile("dup.txt");
        var existing = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = f, Name = "dup.txt" }
        };

        var result = _service.CreateShelfItems(new() { f }, existing);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void CreateShelfItems_DeduplicatesWithinSameCall()
    {
        var f = MakeFile("once.txt");
        var existing = new ObservableCollection<ShelfItem>();

        var result = _service.CreateShelfItems(new() { f, f, f }, existing);

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void CreateShelfItems_DuplicateCheckIsCaseInsensitive()
    {
        var f = MakeFile("Case.TXT");
        var existing = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = f.ToUpperInvariant() }
        };

        var result = _service.CreateShelfItems(new() { f.ToLowerInvariant() }, existing);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void CreateShelfItems_AddsIconGlyphFromIconService()
    {
        var f = MakeFile("a.txt");
        var d = MakeDir("folderX");

        var result = _service.CreateShelfItems(new() { f, d }, new());

        Assert.IsFalse(string.IsNullOrEmpty(result[0].IconGlyph));
        Assert.AreEqual(_icons.FolderGlyph, result[1].IconGlyph);
    }

    // ── ValidateItems ───────────────────────────────

    [TestMethod]
    public void ValidateItems_ReturnsOnlyMissing()
    {
        var existing = MakeFile("here.txt");
        var ghost = Path.Combine(_tempDir, "ghost.txt");
        var col = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = existing },
            new ShelfItem { Path = ghost }
        };

        var missing = ShelfService.ValidateItems(col);

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual(ghost, missing[0].Path);
    }

    [TestMethod]
    public void ValidateItems_EmptyCollection_ReturnsEmpty()
    {
        var missing = ShelfService.ValidateItems(new());
        Assert.AreEqual(0, missing.Count);
    }

    // ── GetPaths ────────────────────────────────────

    [TestMethod]
    public void GetPaths_ReturnsAllPathsInOrder()
    {
        var col = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = "a" },
            new ShelfItem { Path = "b" },
            new ShelfItem { Path = "c" }
        };

        var paths = ShelfService.GetPaths(col);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, paths);
    }

    // ── Save / Load ─────────────────────────────────

    [TestMethod]
    public void SaveAndLoad_RoundTrip_PreservesPathsAndPin()
    {
        var f1 = MakeFile("one.txt");
        var f2 = MakeFile("two.txt");
        var col = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = f1, IsPinned = true },
            new ShelfItem { Path = f2, IsPinned = false }
        };

        _service.SaveShelfItems(col);
        var loaded = _service.LoadShelfItems();

        Assert.AreEqual(2, loaded.Count);
        var pinned = loaded.First(i => i.Path == f1);
        var notPinned = loaded.First(i => i.Path == f2);
        Assert.IsTrue(pinned.IsPinned);
        Assert.IsFalse(notPinned.IsPinned);
    }

    [TestMethod]
    public void Load_NoSavedData_ReturnsEmpty()
    {
        var loaded = _service.LoadShelfItems();
        Assert.AreEqual(0, loaded.Count);
    }

    [TestMethod]
    public void Load_OldListStringFormat_StillReadable()
    {
        var f = MakeFile("legacy.txt");
        // 옛 포맷: List<string> JSON
        var legacyJson = JsonSerializer.Serialize(new List<string> { f });
        _settings.Set("ShelfItemsJson", legacyJson);

        var loaded = _service.LoadShelfItems();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(f, loaded[0].Path);
        Assert.IsFalse(loaded[0].IsPinned);
    }

    [TestMethod]
    public void Load_DropsMissingFilesOnReload()
    {
        var f1 = MakeFile("alive.txt");
        var ghost = Path.Combine(_tempDir, "deleted.txt");
        var col = new ObservableCollection<ShelfItem>
        {
            new ShelfItem { Path = f1 },
            new ShelfItem { Path = ghost }
        };
        _service.SaveShelfItems(col);

        var loaded = _service.LoadShelfItems();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(f1, loaded[0].Path);
    }

    [TestMethod]
    public void Save_EmptyCollection_LoadsEmpty()
    {
        _service.SaveShelfItems(new());
        var loaded = _service.LoadShelfItems();
        Assert.AreEqual(0, loaded.Count);
    }

    // ── Constants ───────────────────────────────────

    [TestMethod]
    public void MaxShelfItems_Is50()
    {
        Assert.AreEqual(50, ShelfService.MaxShelfItems);
    }
}
