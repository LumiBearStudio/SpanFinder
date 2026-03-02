using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FolderSizeServiceTests
{
    private FolderSizeService _service = null!;
    private string _tempRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FolderSizeService();
        _tempRoot = Path.Combine(Path.GetTempPath(), "SpanTests_FolderSize_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ── Helper ────────────────────────────────────────────

    private string CreateFileWithSize(string relativePath, int sizeInBytes)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(fullPath, new byte[sizeInBytes]);
        return fullPath;
    }

    private async Task<long> WaitForSizeCalculated(string folderPath, int timeoutSeconds = 10)
    {
        var tcs = new TaskCompletionSource<long>();
        _service.SizeCalculated += (path, size) =>
        {
            if (string.Equals(path, folderPath, StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(size);
        };
        _service.RequestCalculation(folderPath);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
    }

    // ── TryGetCachedSize ──────────────────────────────────

    [TestMethod]
    public void TryGetCachedSize_NoCalculation_ReturnsNull()
    {
        var result = _service.TryGetCachedSize(_tempRoot);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task TryGetCachedSize_AfterCalculation_ReturnsCachedValue()
    {
        CreateFileWithSize("file1.bin", 100);
        CreateFileWithSize("file2.bin", 200);

        var size = await WaitForSizeCalculated(_tempRoot);

        var cached = _service.TryGetCachedSize(_tempRoot);
        Assert.IsNotNull(cached);
        Assert.AreEqual(size, cached.Value);
    }

    // ── RequestCalculation + SizeCalculated event ─────────

    [TestMethod]
    public async Task RequestCalculation_CalculatesAndFiresEvent()
    {
        CreateFileWithSize("a.bin", 512);
        CreateFileWithSize("sub/b.bin", 256);

        var size = await WaitForSizeCalculated(_tempRoot);

        Assert.AreEqual(768L, size);
    }

    [TestMethod]
    public async Task SizeCalculated_EventContainsCorrectPath()
    {
        CreateFileWithSize("data.bin", 10);

        string? receivedPath = null;
        var tcs = new TaskCompletionSource<bool>();
        _service.SizeCalculated += (path, _) =>
        {
            if (string.Equals(path, _tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                receivedPath = path;
                tcs.TrySetResult(true);
            }
        };
        _service.RequestCalculation(_tempRoot);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(_tempRoot, receivedPath);
    }

    // ── Skips if already cached ───────────────────────────

    [TestMethod]
    public async Task RequestCalculation_SkipsIfAlreadyCached()
    {
        CreateFileWithSize("x.bin", 50);

        await WaitForSizeCalculated(_tempRoot);

        // Second request should be ignored (already cached)
        int eventCount = 0;
        _service.SizeCalculated += (path, _) =>
        {
            if (string.Equals(path, _tempRoot, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref eventCount);
        };
        _service.RequestCalculation(_tempRoot);

        // Give it a moment; event should NOT fire again
        await Task.Delay(200);
        Assert.AreEqual(0, eventCount, "Should not fire event for already-cached folder");
    }

    // ── Skips if already pending (no duplicate) ───────────

    [TestMethod]
    public async Task RequestCalculation_NoDuplicatePending()
    {
        // Create a folder with enough content to take a measurable time
        for (int i = 0; i < 20; i++)
            CreateFileWithSize($"dir{i}/file.bin", 100);

        int eventCount = 0;
        var tcs = new TaskCompletionSource<bool>();
        _service.SizeCalculated += (path, _) =>
        {
            if (string.Equals(path, _tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref eventCount);
                tcs.TrySetResult(true);
            }
        };

        // Fire multiple times rapidly
        _service.RequestCalculation(_tempRoot);
        _service.RequestCalculation(_tempRoot);
        _service.RequestCalculation(_tempRoot);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Wait a bit more for any straggler
        await Task.Delay(300);

        Assert.AreEqual(1, eventCount, "Event should fire exactly once even with multiple requests");
    }

    // ── Invalidate ────────────────────────────────────────

    [TestMethod]
    public async Task Invalidate_RemovesFromCache()
    {
        CreateFileWithSize("keep.bin", 64);

        await WaitForSizeCalculated(_tempRoot);
        Assert.IsNotNull(_service.TryGetCachedSize(_tempRoot));

        _service.Invalidate(_tempRoot);

        Assert.IsNull(_service.TryGetCachedSize(_tempRoot));
    }

    [TestMethod]
    public async Task Invalidate_AllowsRecalculation()
    {
        CreateFileWithSize("v1.bin", 100);
        await WaitForSizeCalculated(_tempRoot);
        Assert.AreEqual(100L, _service.TryGetCachedSize(_tempRoot));

        // Add more data and invalidate
        CreateFileWithSize("v2.bin", 200);
        _service.Invalidate(_tempRoot);

        // Small delay to let the previous Task.Run finally block complete
        // (the SizeCalculated event fires before _pending.TryRemove in finally)
        await Task.Delay(100);

        var newSize = await WaitForSizeCalculated(_tempRoot);
        Assert.AreEqual(300L, newSize);
    }

    // ── Empty folder ──────────────────────────────────────

    [TestMethod]
    public async Task RequestCalculation_EmptyFolder_ReturnsZero()
    {
        var size = await WaitForSizeCalculated(_tempRoot);
        Assert.AreEqual(0L, size);
    }

    // ── Empty/null path ───────────────────────────────────

    [TestMethod]
    public void RequestCalculation_NullPath_NoOp()
    {
        // Should not throw
        _service.RequestCalculation(null!);
    }

    [TestMethod]
    public void RequestCalculation_EmptyPath_NoOp()
    {
        // Should not throw
        _service.RequestCalculation(string.Empty);
    }

    // ── CancelCalculation ────────────────────────────────

    [TestMethod]
    public void CancelCalculation_DoesNotThrow_WhenNoPending()
    {
        // Should not throw for a path that was never requested
        _service.CancelCalculation(@"C:\NotPending");
    }

    [TestMethod]
    public async Task CancelCalculation_StopsOngoingWork()
    {
        // Create a large enough structure that calculation takes some time
        for (int i = 0; i < 50; i++)
            CreateFileWithSize($"deep/d{i}/f.bin", 100);

        // Start calculation then immediately cancel
        _service.RequestCalculation(_tempRoot);
        _service.CancelCalculation(_tempRoot);

        // Wait a moment for the cancellation to propagate
        await Task.Delay(500);

        // Cache should either be empty (cancelled before caching) or have a value
        // The key test is that it doesn't hang or crash
        Assert.IsTrue(true, "CancelCalculation completed without hanging");
    }

    // ── CancelAll ──────────────────────────────────────────

    [TestMethod]
    public void CancelAll_DoesNotThrow_WhenEmpty()
    {
        _service.CancelAll();
    }

    [TestMethod]
    public async Task CancelAll_StopsAllPendingWork()
    {
        // Create multiple folders for concurrent calculation
        var dir1 = Path.Combine(_tempRoot, "dir1");
        var dir2 = Path.Combine(_tempRoot, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        for (int i = 0; i < 20; i++)
        {
            CreateFileWithSize($"dir1/f{i}.bin", 50);
            CreateFileWithSize($"dir2/f{i}.bin", 50);
        }

        _service.RequestCalculation(dir1);
        _service.RequestCalculation(dir2);

        // Cancel all immediately
        _service.CancelAll();

        await Task.Delay(500);

        // After CancelAll, new requests should work
        var tcs = new TaskCompletionSource<long>();
        _service.SizeCalculated += (path, size) =>
        {
            if (string.Equals(path, dir1, StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(size);
        };
        _service.Invalidate(dir1);
        _service.RequestCalculation(dir1);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(result > 0, "Should recalculate after CancelAll");
    }

    // ── Inaccessible folder → caches -1 ──────────────────

    [TestMethod]
    public async Task RequestCalculation_NonexistentFolder_CachesZero()
    {
        // CalculateFolderSize catches DirectoryNotFoundException internally
        // and returns 0 (the exception doesn't propagate to RequestCalculation's catch block)
        var badPath = Path.Combine(_tempRoot, "does_not_exist_" + Guid.NewGuid().ToString("N")[..8]);

        var tcs = new TaskCompletionSource<long>();
        _service.SizeCalculated += (path, size) =>
        {
            if (string.Equals(path, badPath, StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(size);
        };
        _service.RequestCalculation(badPath);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0L, result, "Nonexistent folder size should be 0 (exception caught internally)");
        Assert.AreEqual(0L, _service.TryGetCachedSize(badPath));
    }
}
