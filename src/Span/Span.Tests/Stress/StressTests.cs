using Span.Models;
using Span.Helpers;
using Span.Services;
using Span.Services.FileOperations;
using Moq;

namespace Span.Tests.Stress;

[TestClass]
[TestCategory("Stress")]
public class StressTests
{
    // ── Helpers ──────────────────────────────────────────────

    private static List<FolderItem> MakeFolders(string tempDir, int count)
    {
        var list = new List<FolderItem>();
        for (int i = 0; i < count; i++)
            list.Add(new FolderItem { Name = $"Folder_{i:D4}", Path = Path.Combine(tempDir, $"Folder_{i:D4}") });
        return list;
    }

    private static List<FileItem> MakeFiles(string tempDir, int count)
    {
        var list = new List<FileItem>();
        for (int i = 0; i < count; i++)
            list.Add(new FileItem { Name = $"file_{i:D4}.txt", Path = Path.Combine(tempDir, $"file_{i:D4}.txt") });
        return list;
    }

    private static Mock<IFileOperation> CreateSuccessOp(string description = "stress op")
    {
        var mock = new Mock<IFileOperation>();
        mock.Setup(o => o.Description).Returns(description);
        mock.Setup(o => o.CanUndo).Returns(true);
        mock.Setup(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
        mock.Setup(o => o.UndoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
        return mock;
    }

    // ── 1. Stress_FolderContentCache_1000Entries ─────────────

    [TestMethod]
    public void Stress_FolderContentCache_1000Entries()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SpanStress_Cache_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempRoot);

            var cache = new FolderContentCache();

            // Create 1000 subdirectories and cache entries
            // FolderContentCache has MaxEntries=500, so older entries get evicted
            for (int i = 0; i < 1000; i++)
            {
                var subDir = Path.Combine(tempRoot, $"dir_{i:D4}");
                Directory.CreateDirectory(subDir);
                var folders = MakeFolders(subDir, 2);
                var files = MakeFiles(subDir, 3);
                cache.Set(subDir, folders, files, showHidden: false);
            }

            // MaxEntries=500 with 10% eviction — cache should be at or under 500
            Assert.IsTrue(cache.Count <= 500, $"Cache should respect MaxEntries limit, got {cache.Count}");
            Assert.IsTrue(cache.Count > 0, "Cache should not be empty");

            // Verify that cached entries return valid data when found
            // ConcurrentDictionary eviction order is non-deterministic, so we check
            // that a majority of entries are retrievable, not specific indices
            int hitCount = 0;
            for (int i = 0; i < 1000; i++)
            {
                var subDir = Path.Combine(tempRoot, $"dir_{i:D4}");
                var result = cache.TryGet(subDir, showHidden: false);
                if (result != null)
                {
                    hitCount++;
                    Assert.AreEqual(2, result.Folders.Count);
                    Assert.AreEqual(3, result.Files.Count);
                }
            }

            // Should have at least 400 cache hits (cache holds ~450-500 after eviction)
            Assert.IsTrue(hitCount >= 400, $"Expected at least 400 cache hits, got {hitCount}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── 2. Stress_FolderSizeService_ConcurrentCalc ──────────

    [TestMethod]
    public async Task Stress_FolderSizeService_ConcurrentCalc()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SpanStress_FolderSize_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempRoot);

            var service = new FolderSizeService();
            const int folderCount = 100;

            // Create 100 folders each with a single file of known size
            for (int i = 0; i < folderCount; i++)
            {
                var dir = Path.Combine(tempRoot, $"folder_{i:D3}");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "data.bin"), new byte[1024]);
            }

            // Request calculations for all 100 concurrently
            var completionTasks = new List<Task<long>>();
            for (int i = 0; i < folderCount; i++)
            {
                var dir = Path.Combine(tempRoot, $"folder_{i:D3}");
                var tcs = new TaskCompletionSource<long>();
                var captured = dir;
                service.SizeCalculated += (path, size) =>
                {
                    if (string.Equals(path, captured, StringComparison.OrdinalIgnoreCase))
                        tcs.TrySetResult(size);
                };
                service.RequestCalculation(dir);
                completionTasks.Add(tcs.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            }

            var results = await Task.WhenAll(completionTasks);

            // All should report 1024 bytes
            foreach (var size in results)
            {
                Assert.AreEqual(1024L, size, "Each folder should contain exactly 1024 bytes");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── 3. Stress_SearchQueryParser_1000Queries ─────────────

    [TestMethod]
    public void Stress_SearchQueryParser_1000Queries()
    {
        var queries = new[]
        {
            "hello", "kind:image", "size:>1MB", "date:today", "ext:.pdf",
            "kind:video vacation", "size:large date:thisweek", "\"my document\"",
            "kind:audio size:>500KB ext:.mp3 concert", "size:empty",
            "kind:code ext:.cs", "date:>2024-01-01", "kind:archive size:<100MB",
            "kind:document annual report", "EXT:.xlsx", "SIZE:>=10MB",
            "kind:exe", "date:lastmonth", "kind:font ext:.ttf", "size:tiny"
        };

        var rng = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            var queryStr = queries[rng.Next(queries.Length)];
            var result = SearchQueryParser.Parse(queryStr);
            Assert.IsFalse(result.IsEmpty, $"Query '{queryStr}' should not be empty");
        }
    }

    // ── 4. Stress_FileOperationHistory_RapidUndoRedo ────────

    [TestMethod]
    public async Task Stress_FileOperationHistory_RapidUndoRedo()
    {
        var history = new FileOperationHistory();
        const int opCount = 100;

        // Execute 100 operations
        for (int i = 0; i < opCount; i++)
        {
            var op = CreateSuccessOp($"stress op {i}");
            var result = await history.ExecuteAsync(op.Object);
            Assert.IsTrue(result.Success, $"Execute #{i} should succeed");
        }

        Assert.IsTrue(history.CanUndo);
        Assert.IsFalse(history.CanRedo);

        // Undo all 100 (history default MaxHistorySize is 50, so we may undo fewer)
        int undoCount = 0;
        while (history.CanUndo)
        {
            var result = await history.UndoAsync();
            Assert.IsTrue(result.Success, $"Undo #{undoCount} should succeed");
            undoCount++;
        }

        Assert.IsTrue(undoCount > 0, "Should have undone at least one operation");
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);

        // Redo all
        int redoCount = 0;
        while (history.CanRedo)
        {
            var result = await history.RedoAsync();
            Assert.IsTrue(result.Success, $"Redo #{redoCount} should succeed");
            redoCount++;
        }

        Assert.AreEqual(undoCount, redoCount, "Redo count should match undo count");
        Assert.IsTrue(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
    }

    // ── 5. Stress_NaturalStringComparer_10000Sort ───────────

    [TestMethod]
    public void Stress_NaturalStringComparer_10000Sort()
    {
        var rng = new Random(42);
        var names = new List<string>(10000);

        var prefixes = new[] { "file", "doc", "image", "Photo", "Report", "backup", "log", "data" };
        var extensions = new[] { ".txt", ".jpg", ".png", ".pdf", ".cs", ".log", ".zip", ".docx" };

        for (int i = 0; i < 10000; i++)
        {
            var prefix = prefixes[rng.Next(prefixes.Length)];
            var number = rng.Next(1, 100000);
            var ext = extensions[rng.Next(extensions.Length)];
            names.Add($"{prefix}{number}{ext}");
        }

        var comparer = NaturalStringComparer.Instance;

        // Sort should not throw and should complete quickly
        names.Sort(comparer);

        // Verify sort order: each element should be <= next element
        for (int i = 0; i < names.Count - 1; i++)
        {
            var cmp = comparer.Compare(names[i], names[i + 1]);
            Assert.IsTrue(cmp <= 0, $"Sort order violated at index {i}: '{names[i]}' > '{names[i + 1]}'");
        }
    }

    // ── 6. Stress_LargeFolder_10000Files_Load ───────────────

    [TestMethod]
    [TestCategory("Integration")]
    public void Stress_LargeFolder_10000Files_Load()
    {
        if (!Directory.Exists(TestFixtureHelper.TestRoot))
            Assert.Inconclusive("E:\\TEST not available");

        TestFixtureHelper.EnsureTestFixtures();
        TestFixtureHelper.EnsureStressTestFolder(10000);

        var stressDir = TestFixtureHelper.StressTest;
        Assert.IsTrue(Directory.Exists(stressDir), "StressTest folder should exist");

        // Read all files using Directory.EnumerateFiles (simulates what FileSystemService does)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = Directory.EnumerateFiles(stressDir).ToList();
        sw.Stop();

        Assert.IsTrue(files.Count >= 10000, $"Expected at least 10000 files, got {files.Count}");

        // Cache all as FileItems
        var cache = new FolderContentCache();
        var fileItems = files.Select(f => new FileItem
        {
            Name = Path.GetFileName(f),
            Path = f,
            Size = new FileInfo(f).Length
        }).ToList();

        cache.Set(stressDir, new List<FolderItem>(), fileItems, showHidden: false);
        Assert.AreEqual(1, cache.Count);

        var result = cache.TryGet(stressDir, showHidden: false);
        Assert.IsNotNull(result);
        Assert.AreEqual(fileItems.Count, result.Files.Count);
    }

    // ── 7. Stress_ConcurrentFileOps_5Parallel ───────────────

    [TestMethod]
    public async Task Stress_ConcurrentFileOps_5Parallel()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SpanStress_ConcurrentOps_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempRoot);

            const int parallelCount = 5;
            const int filesPerOp = 20;

            var tasks = new List<Task>();

            for (int p = 0; p < parallelCount; p++)
            {
                var sourceDir = Path.Combine(tempRoot, $"source_{p}");
                var destDir = Path.Combine(tempRoot, $"dest_{p}");
                Directory.CreateDirectory(sourceDir);
                Directory.CreateDirectory(destDir);

                // Create source files
                var sourcePaths = new List<string>();
                for (int i = 0; i < filesPerOp; i++)
                {
                    var filePath = Path.Combine(sourceDir, $"file_{i:D3}.txt");
                    File.WriteAllText(filePath, $"Content for parallel {p}, file {i}");
                    sourcePaths.Add(filePath);
                }

                // Run copy operation
                var captured = (sourcePaths, destDir);
                tasks.Add(Task.Run(async () =>
                {
                    var op = new CopyFileOperation(captured.sourcePaths, captured.destDir);
                    var result = await op.ExecuteAsync();
                    Assert.IsTrue(result.Success, $"Copy operation should succeed");
                }));
            }

            await Task.WhenAll(tasks);

            // Verify all copies completed
            for (int p = 0; p < parallelCount; p++)
            {
                var destDir = Path.Combine(tempRoot, $"dest_{p}");
                var copiedFiles = Directory.GetFiles(destDir);
                Assert.AreEqual(filesPerOp, copiedFiles.Length,
                    $"Destination {p} should contain {filesPerOp} files, got {copiedFiles.Length}");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
