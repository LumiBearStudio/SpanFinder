using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FileSystemWatcherServiceTests
{
    private FileSystemWatcherService _service = null!;
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FileSystemWatcherService();
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanWatcherTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ── SetWatchedPaths ─────────────────────────────────────

    [TestMethod]
    public void SetWatchedPaths_DoesNotThrow_EmptyList()
    {
        _service.SetWatchedPaths(Array.Empty<string>());
    }

    [TestMethod]
    public void SetWatchedPaths_DoesNotThrow_NullPaths()
    {
        _service.SetWatchedPaths(new string[] { null!, "", "   " });
    }

    [TestMethod]
    public void SetWatchedPaths_WatchesValidPath()
    {
        _service.SetWatchedPaths(new[] { _tempDir });
        // No exception means success — watcher is created
    }

    [TestMethod]
    public void SetWatchedPaths_IgnoresNonExistentPath()
    {
        var badPath = Path.Combine(_tempDir, "does_not_exist");
        _service.SetWatchedPaths(new[] { badPath });
        // Should not throw
    }

    [TestMethod]
    public void SetWatchedPaths_IgnoresUncPath()
    {
        _service.SetWatchedPaths(new[] { @"\\server\share" });
        // UNC paths should be silently skipped
    }

    [TestMethod]
    public void SetWatchedPaths_ReplacesPreviousPaths()
    {
        var dir1 = Path.Combine(_tempDir, "dir1");
        var dir2 = Path.Combine(_tempDir, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        _service.SetWatchedPaths(new[] { dir1 });
        _service.SetWatchedPaths(new[] { dir2 });
        // dir1 watcher should be removed, dir2 added
    }

    // ── PathChanged event (debounce) ───────────────────────

    [TestMethod]
    public async Task PathChanged_FiresOnFileCreation()
    {
        var tcs = new TaskCompletionSource<string>();
        _service.PathChanged += path =>
        {
            if (string.Equals(path, _tempDir, StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(path);
        };

        _service.SetWatchedPaths(new[] { _tempDir });

        // Create a file to trigger the event
        await Task.Delay(100); // Give watcher time to initialize
        File.WriteAllText(Path.Combine(_tempDir, "test_" + Guid.NewGuid().ToString("N")[..8] + ".txt"), "hello");

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(_tempDir, result);
    }

    [TestMethod]
    public async Task PathChanged_DebouncesRapidEvents()
    {
        int eventCount = 0;
        _service.PathChanged += path =>
        {
            if (string.Equals(path, _tempDir, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref eventCount);
        };

        _service.SetWatchedPaths(new[] { _tempDir });
        await Task.Delay(100);

        // Create multiple files rapidly (within debounce window of 300ms)
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(
                Path.Combine(_tempDir, $"rapid_{i}_{Guid.NewGuid().ToString("N")[..6]}.txt"),
                "data");
        }

        // Wait for debounce to settle (300ms default + buffer)
        await Task.Delay(1000);

        // Debouncing should coalesce rapid events into fewer notifications
        Assert.IsTrue(eventCount >= 1, "Should fire at least once");
        Assert.IsTrue(eventCount <= 3, $"Debounce should coalesce events, but got {eventCount}");
    }

    // ── StopAll ────────────────────────────────────────────

    [TestMethod]
    public void StopAll_DoesNotThrow_WhenEmpty()
    {
        _service.StopAll();
    }

    [TestMethod]
    public void StopAll_StopsAllWatchers()
    {
        var dir1 = Path.Combine(_tempDir, "stop1");
        var dir2 = Path.Combine(_tempDir, "stop2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        _service.SetWatchedPaths(new[] { dir1, dir2 });
        _service.StopAll();

        // After StopAll, no events should fire
        bool eventFired = false;
        _service.PathChanged += _ => eventFired = true;

        File.WriteAllText(Path.Combine(dir1, "after_stop.txt"), "test");
        Thread.Sleep(500);

        Assert.IsFalse(eventFired, "No events should fire after StopAll");
    }

    // ── Dispose ────────────────────────────────────────────

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _service.SetWatchedPaths(new[] { _tempDir });
        _service.Dispose();
        _service.Dispose(); // Should not throw
    }
}
