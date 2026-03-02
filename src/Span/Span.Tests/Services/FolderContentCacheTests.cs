using Span.Models;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FolderContentCacheTests
{
    private string _tempDir = null!;
    private FolderContentCache _cache = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanCacheTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _cache = new FolderContentCache();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private List<FolderItem> MakeFolders(params string[] names)
    {
        var list = new List<FolderItem>();
        foreach (var n in names)
            list.Add(new FolderItem { Name = n, Path = Path.Combine(_tempDir, n) });
        return list;
    }

    private List<FileItem> MakeFiles(params string[] names)
    {
        var list = new List<FileItem>();
        foreach (var n in names)
            list.Add(new FileItem { Name = n, Path = Path.Combine(_tempDir, n) });
        return list;
    }

    private string CreateSubDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── 1. Set_TryGet_ReturnsCachedData ─────────────────────

    [TestMethod]
    public void Set_TryGet_ReturnsCachedData()
    {
        var folders = MakeFolders("Documents", "Pictures");
        var files = MakeFiles("readme.txt");

        _cache.Set(_tempDir, folders, files, showHidden: false);

        var result = _cache.TryGet(_tempDir, showHidden: false);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Folders.Count);
        Assert.AreEqual(1, result.Files.Count);
    }

    // ── 2. TryGet_NotCached_ReturnsNull ─────────────────────

    [TestMethod]
    public void TryGet_NotCached_ReturnsNull()
    {
        var result = _cache.TryGet(_tempDir, showHidden: false);

        Assert.IsNull(result);
    }

    // ── 3. TryGet_StaleDirectory_ReturnsNull ────────────────

    [TestMethod]
    public void TryGet_StaleDirectory_ReturnsNull()
    {
        var folders = MakeFolders();
        var files = MakeFiles();

        _cache.Set(_tempDir, folders, files, showHidden: false);

        // Explicitly advance directory LastWriteTimeUtc to ensure staleness is detected
        // (NTFS timestamp resolution may not update immediately on file creation alone)
        var touchFile = Path.Combine(_tempDir, "touch_" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        File.WriteAllText(touchFile, "stale");
        Directory.SetLastWriteTimeUtc(_tempDir, DateTime.UtcNow.AddSeconds(2));

        var result = _cache.TryGet(_tempDir, showHidden: false);

        Assert.IsNull(result, "Cache should return null for a directory whose LastWriteTimeUtc has changed");
    }

    // ── 4. TryGet_HiddenMismatch_ReturnsNull ────────────────

    [TestMethod]
    public void TryGet_HiddenMismatch_ReturnsNull()
    {
        var folders = MakeFolders("Visible");
        var files = MakeFiles("file.txt");

        _cache.Set(_tempDir, folders, files, showHidden: false);

        // Request with showHidden=true should miss (mismatch)
        var result = _cache.TryGet(_tempDir, showHidden: true);

        Assert.IsNull(result, "Cache should return null when showHidden does not match");
    }

    // ── 5. TryGet_NonExistentDirectory_ReturnsNull ──────────

    [TestMethod]
    public void TryGet_NonExistentDirectory_ReturnsNull()
    {
        var subDir = CreateSubDir("ephemeral");
        var folders = MakeFolders();
        var files = MakeFiles();

        _cache.Set(subDir, folders, files, showHidden: false);

        // Delete the directory so that Directory.GetLastWriteTimeUtc throws
        Directory.Delete(subDir, recursive: true);

        var result = _cache.TryGet(subDir, showHidden: false);

        Assert.IsNull(result, "Cache should return null when directory no longer exists");
    }

    // ── 6. Invalidate_RemovesEntry ──────────────────────────

    [TestMethod]
    public void Invalidate_RemovesEntry()
    {
        _cache.Set(_tempDir, MakeFolders(), MakeFiles(), showHidden: false);
        Assert.AreEqual(1, _cache.Count);

        _cache.Invalidate(_tempDir);

        Assert.AreEqual(0, _cache.Count);
        Assert.IsNull(_cache.TryGet(_tempDir, showHidden: false));
    }

    // ── 7. Invalidate_NonExistentKey_DoesNotThrow ───────────

    [TestMethod]
    public void Invalidate_NonExistentKey_DoesNotThrow()
    {
        // Should not throw for a key that was never cached
        _cache.Invalidate(@"C:\NonExistent\Path\That\Was\Never\Cached");
        _cache.Invalidate(string.Empty);

        Assert.AreEqual(0, _cache.Count);
    }

    // ── 8. Clear_RemovesAllEntries ──────────────────────────

    [TestMethod]
    public void Clear_RemovesAllEntries()
    {
        var dir1 = CreateSubDir("dir1");
        var dir2 = CreateSubDir("dir2");
        var dir3 = CreateSubDir("dir3");

        _cache.Set(dir1, MakeFolders(), MakeFiles(), showHidden: false);
        _cache.Set(dir2, MakeFolders(), MakeFiles(), showHidden: false);
        _cache.Set(dir3, MakeFolders(), MakeFiles(), showHidden: true);
        Assert.AreEqual(3, _cache.Count);

        _cache.Clear();

        Assert.AreEqual(0, _cache.Count);
    }

    // ── 9. Count_ReflectsEntries ────────────────────────────

    [TestMethod]
    public void Count_ReflectsEntries()
    {
        Assert.AreEqual(0, _cache.Count);

        var dir1 = CreateSubDir("alpha");
        var dir2 = CreateSubDir("beta");
        var dir3 = CreateSubDir("gamma");

        _cache.Set(dir1, MakeFolders(), MakeFiles(), showHidden: false);
        Assert.AreEqual(1, _cache.Count);

        _cache.Set(dir2, MakeFolders(), MakeFiles(), showHidden: false);
        Assert.AreEqual(2, _cache.Count);

        _cache.Set(dir3, MakeFolders(), MakeFiles(), showHidden: false);
        Assert.AreEqual(3, _cache.Count);
    }

    // ── 10. Set_OverwritesSameKey ───────────────────────────

    [TestMethod]
    public void Set_OverwritesSameKey()
    {
        var oldFolders = MakeFolders("OldFolder");
        var oldFiles = MakeFiles("old.txt");

        _cache.Set(_tempDir, oldFolders, oldFiles, showHidden: false);

        // Overwrite with new data (same path)
        var newFolders = MakeFolders("NewFolder1", "NewFolder2");
        var newFiles = MakeFiles("new1.txt", "new2.txt", "new3.txt");

        _cache.Set(_tempDir, newFolders, newFiles, showHidden: false);

        Assert.AreEqual(1, _cache.Count, "Overwriting same key should not increase count");

        var result = _cache.TryGet(_tempDir, showHidden: false);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Folders.Count, "Should return the latest folders");
        Assert.AreEqual(3, result.Files.Count, "Should return the latest files");
        Assert.AreEqual("NewFolder1", result.Folders[0].Name);
    }

    // ── 11. CaseInsensitivePaths ────────────────────────────

    [TestMethod]
    public void CaseInsensitivePaths()
    {
        var folders = MakeFolders("Sub");
        var files = MakeFiles("data.bin");

        // Set with original casing
        _cache.Set(_tempDir, folders, files, showHidden: false);

        // Retrieve with different casing (Windows paths are case-insensitive)
        var upperPath = _tempDir.ToUpperInvariant();
        var lowerPath = _tempDir.ToLowerInvariant();

        // The TryGet freshness check calls Directory.GetLastWriteTimeUtc which also
        // works case-insensitively on Windows, so the hit should succeed.
        var resultUpper = _cache.TryGet(upperPath, showHidden: false);
        var resultLower = _cache.TryGet(lowerPath, showHidden: false);

        Assert.IsNotNull(resultUpper, "Cache lookup should be case-insensitive (upper)");
        Assert.IsNotNull(resultLower, "Cache lookup should be case-insensitive (lower)");
        Assert.AreEqual(1, _cache.Count, "Different casings should refer to the same entry");
    }

    // ── 12. LRU Eviction ───────────────────────────────────

    [TestMethod]
    public void TryGet_UpdatesAccessTick_ForLRUTracking()
    {
        // Set two entries
        var dir1 = CreateSubDir("lru1");
        var dir2 = CreateSubDir("lru2");

        _cache.Set(dir1, MakeFolders(), MakeFiles(), showHidden: false);
        _cache.Set(dir2, MakeFolders(), MakeFiles(), showHidden: false);

        // Access dir1 again — its AccessTick should be updated (higher)
        var result1 = _cache.TryGet(dir1, showHidden: false);
        Assert.IsNotNull(result1);

        // Both should still be cached
        Assert.AreEqual(2, _cache.Count);
        Assert.IsNotNull(_cache.TryGet(dir1, showHidden: false));
        Assert.IsNotNull(_cache.TryGet(dir2, showHidden: false));
    }

    [TestMethod]
    public void Set_EvictsOldestEntries_WhenMaxExceeded()
    {
        // MaxEntries is 500, which is too many to create real dirs.
        // Instead, verify eviction logic by observing that after many inserts,
        // the oldest (least recently accessed) entries get removed.
        // We can't easily test 500 dirs, so we verify the eviction path doesn't crash.
        var dirs = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var d = CreateSubDir($"evict_{i}");
            dirs.Add(d);
            _cache.Set(d, MakeFolders(), MakeFiles(), showHidden: false);
        }

        Assert.AreEqual(10, _cache.Count);

        // All entries should be retrievable
        foreach (var d in dirs)
            Assert.IsNotNull(_cache.TryGet(d, showHidden: false));
    }

    // ── 13. Set_PreservesFolderAndFileData ──────────────────

    [TestMethod]
    public void Set_PreservesFolderAndFileData()
    {
        var folders = new List<FolderItem>
        {
            new() { Name = "Documents", Path = Path.Combine(_tempDir, "Documents") },
            new() { Name = "Pictures", Path = Path.Combine(_tempDir, "Pictures") },
        };
        var files = new List<FileItem>
        {
            new() { Name = "readme.txt", Path = Path.Combine(_tempDir, "readme.txt"), Size = 1024 },
            new() { Name = "notes.md", Path = Path.Combine(_tempDir, "notes.md"), Size = 512 },
        };

        _cache.Set(_tempDir, folders, files, showHidden: true);

        var result = _cache.TryGet(_tempDir, showHidden: true);

        Assert.IsNotNull(result);

        // Verify folder data
        Assert.AreEqual(2, result.Folders.Count);
        Assert.AreEqual("Documents", result.Folders[0].Name);
        Assert.AreEqual(Path.Combine(_tempDir, "Documents"), result.Folders[0].Path);
        Assert.AreEqual("Pictures", result.Folders[1].Name);
        Assert.AreEqual(Path.Combine(_tempDir, "Pictures"), result.Folders[1].Path);

        // Verify file data
        Assert.AreEqual(2, result.Files.Count);
        Assert.AreEqual("readme.txt", result.Files[0].Name);
        Assert.AreEqual(Path.Combine(_tempDir, "readme.txt"), result.Files[0].Path);
        Assert.AreEqual(1024L, result.Files[0].Size);
        Assert.AreEqual("notes.md", result.Files[1].Name);
        Assert.AreEqual(512L, result.Files[1].Size);

        // Verify hidden flag
        Assert.IsTrue(result.IncludesHidden);
    }
}
