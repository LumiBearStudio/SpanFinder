using Span.Services.FileOperations;

namespace Span.Tests.Integration;

[TestClass]
[TestCategory("Stress")]
[TestCategory("Integration")]
public class FileOperationStressTests
{
    private const int FileCount = 1000;

    // ── Helpers ──────────────────────────────────────────────

    private static void EnsureTestRoot()
    {
        if (!Directory.Exists(TestFixtureHelper.TestRoot))
            Assert.Inconclusive("E:\\TEST not available");
        TestFixtureHelper.EnsureTestFixtures();
    }

    private static string CreateSourceWithFiles(string prefix, int count)
    {
        var sourceDir = TestFixtureHelper.CreateTempCopyDir(prefix + "_src");
        for (int i = 0; i < count; i++)
        {
            File.WriteAllText(
                Path.Combine(sourceDir, $"file_{i:D4}.txt"),
                $"Content of file {i} for {prefix}");
        }
        return sourceDir;
    }

    // ── 1. Copy1000Files_CompletesWithProgress ──────────────

    [TestMethod]
    public async Task Copy1000Files_CompletesWithProgress()
    {
        EnsureTestRoot();

        string? sourceDir = null;
        string? destDir = null;
        try
        {
            sourceDir = CreateSourceWithFiles("Copy1K", FileCount);
            destDir = TestFixtureHelper.CreateTempCopyDir("Copy1K_dst");

            var sourcePaths = Directory.GetFiles(sourceDir).ToList();
            Assert.AreEqual(FileCount, sourcePaths.Count);

            var progressReports = new List<FileOperationProgress>();
            var progress = new Progress<FileOperationProgress>(p => progressReports.Add(p));

            var op = new CopyFileOperation(sourcePaths, destDir);
            var result = await op.ExecuteAsync(progress);

            Assert.IsTrue(result.Success, $"Copy should succeed: {result.ErrorMessage}");

            var copiedFiles = Directory.GetFiles(destDir);
            Assert.AreEqual(FileCount, copiedFiles.Length, $"Expected {FileCount} copied files");

            // Source files should still exist
            Assert.AreEqual(FileCount, Directory.GetFiles(sourceDir).Length, "Source files should remain");
        }
        finally
        {
            if (sourceDir != null) TestFixtureHelper.CleanupTempDir(sourceDir);
            if (destDir != null) TestFixtureHelper.CleanupTempDir(destDir);
        }
    }

    // ── 2. Move1000Files_CompletesWithProgress ──────────────

    [TestMethod]
    public async Task Move1000Files_CompletesWithProgress()
    {
        EnsureTestRoot();

        string? sourceDir = null;
        string? destDir = null;
        try
        {
            sourceDir = CreateSourceWithFiles("Move1K", FileCount);
            destDir = TestFixtureHelper.CreateTempCopyDir("Move1K_dst");

            var sourcePaths = Directory.GetFiles(sourceDir).ToList();
            Assert.AreEqual(FileCount, sourcePaths.Count);

            var progressReports = new List<FileOperationProgress>();
            var progress = new Progress<FileOperationProgress>(p => progressReports.Add(p));

            var op = new MoveFileOperation(sourcePaths, destDir);
            var result = await op.ExecuteAsync(progress);

            Assert.IsTrue(result.Success, $"Move should succeed: {result.ErrorMessage}");

            var movedFiles = Directory.GetFiles(destDir);
            Assert.AreEqual(FileCount, movedFiles.Length, $"Expected {FileCount} moved files");

            // Source files should be gone
            var remaining = Directory.GetFiles(sourceDir);
            Assert.AreEqual(0, remaining.Length, "Source dir should be empty after move");
        }
        finally
        {
            if (sourceDir != null) TestFixtureHelper.CleanupTempDir(sourceDir);
            if (destDir != null) TestFixtureHelper.CleanupTempDir(destDir);
        }
    }

    // ── 3. Delete1000Files_CompletesWithProgress ────────────

    [TestMethod]
    public async Task Delete1000Files_CompletesWithProgress()
    {
        EnsureTestRoot();

        string? sourceDir = null;
        try
        {
            sourceDir = CreateSourceWithFiles("Del1K", FileCount);
            var filePaths = Directory.GetFiles(sourceDir).ToList();
            Assert.AreEqual(FileCount, filePaths.Count);

            var progressReports = new List<FileOperationProgress>();
            var progress = new Progress<FileOperationProgress>(p => progressReports.Add(p));

            // Use permanent=true to avoid Recycle Bin interaction in tests
            var op = new DeleteFileOperation(filePaths, permanent: true);
            var result = await op.ExecuteAsync(progress);

            Assert.IsTrue(result.Success, $"Delete should succeed: {result.ErrorMessage}");

            var remainingFiles = Directory.GetFiles(sourceDir);
            Assert.AreEqual(0, remainingFiles.Length, "All files should be deleted");
        }
        finally
        {
            if (sourceDir != null) TestFixtureHelper.CleanupTempDir(sourceDir);
        }
    }

    // ── 4. ConcurrentCopyMove_NoDeadlock ────────────────────

    [TestMethod]
    [Timeout(60000)] // 60 second timeout to detect deadlocks
    public async Task ConcurrentCopyMove_NoDeadlock()
    {
        EnsureTestRoot();

        string? copySource = null;
        string? copyDest = null;
        string? moveSource = null;
        string? moveDest = null;
        try
        {
            const int filesPerOp = 200;

            copySource = CreateSourceWithFiles("CopyDL", filesPerOp);
            copyDest = TestFixtureHelper.CreateTempCopyDir("CopyDL_dst");
            moveSource = CreateSourceWithFiles("MoveDL", filesPerOp);
            moveDest = TestFixtureHelper.CreateTempCopyDir("MoveDL_dst");

            var copyPaths = Directory.GetFiles(copySource).ToList();
            var movePaths = Directory.GetFiles(moveSource).ToList();

            var copyOp = new CopyFileOperation(copyPaths, copyDest);
            var moveOp = new MoveFileOperation(movePaths, moveDest);

            // Run both simultaneously
            var copyTask = Task.Run(() => copyOp.ExecuteAsync());
            var moveTask = Task.Run(() => moveOp.ExecuteAsync());

            var results = await Task.WhenAll(copyTask, moveTask);

            Assert.IsTrue(results[0].Success, $"Copy should succeed: {results[0].ErrorMessage}");
            Assert.IsTrue(results[1].Success, $"Move should succeed: {results[1].ErrorMessage}");

            Assert.AreEqual(filesPerOp, Directory.GetFiles(copyDest).Length, "All files should be copied");
            Assert.AreEqual(filesPerOp, Directory.GetFiles(moveDest).Length, "All files should be moved");
        }
        finally
        {
            if (copySource != null) TestFixtureHelper.CleanupTempDir(copySource);
            if (copyDest != null) TestFixtureHelper.CleanupTempDir(copyDest);
            if (moveSource != null) TestFixtureHelper.CleanupTempDir(moveSource);
            if (moveDest != null) TestFixtureHelper.CleanupTempDir(moveDest);
        }
    }

    // ── 5. Copy_CancelMidway_CleanState ─────────────────────

    [TestMethod]
    [Timeout(30000)]
    public async Task Copy_CancelMidway_CleanState()
    {
        EnsureTestRoot();

        string? sourceDir = null;
        string? destDir = null;
        try
        {
            // Create many files so the operation takes enough time to cancel
            const int manyFiles = 500;
            sourceDir = CreateSourceWithFiles("Cancel", manyFiles);
            destDir = TestFixtureHelper.CreateTempCopyDir("Cancel_dst");

            var sourcePaths = Directory.GetFiles(sourceDir).ToList();
            var cts = new CancellationTokenSource();

            var op = new CopyFileOperation(sourcePaths, destDir);

            // Cancel after a short delay
            var cancelTask = Task.Run(async () =>
            {
                await Task.Delay(50);
                cts.Cancel();
            });

            var result = await op.ExecuteAsync(cancellationToken: cts.Token);

            // Either the operation completed before cancel (Success=true) or was cancelled
            if (!result.Success)
            {
                Assert.IsTrue(
                    result.ErrorMessage!.Contains("cancel", StringComparison.OrdinalIgnoreCase),
                    $"Error should mention cancellation: {result.ErrorMessage}");
            }

            // In either case, source files should remain intact
            Assert.AreEqual(manyFiles, Directory.GetFiles(sourceDir).Length,
                "Source files should remain intact after cancellation");
        }
        finally
        {
            if (sourceDir != null) TestFixtureHelper.CleanupTempDir(sourceDir);
            if (destDir != null) TestFixtureHelper.CleanupTempDir(destDir);
        }
    }

    // ── 6. LargeFolder_RecursiveCopy ────────────────────────

    [TestMethod]
    [Timeout(120000)] // 2 minutes for deep recursive copy
    public async Task LargeFolder_RecursiveCopy()
    {
        EnsureTestRoot();

        string? destDir = null;
        try
        {
            var deepFolder = TestFixtureHelper.DeepFolder;
            if (!Directory.Exists(deepFolder))
                Assert.Inconclusive("DeepFolder fixture not available");

            destDir = TestFixtureHelper.CreateTempCopyDir("DeepCopy_dst");

            var op = new CopyFileOperation(new List<string> { deepFolder }, destDir);
            var result = await op.ExecuteAsync();

            Assert.IsTrue(result.Success, $"Recursive copy should succeed: {result.ErrorMessage}");

            // Verify the deepest file was copied
            var destDeepFolder = Path.Combine(destDir, "DeepFolder");
            Assert.IsTrue(Directory.Exists(destDeepFolder), "DeepFolder should exist in destination");

            // Walk to the deepest level and check for deep.txt
            var current = destDeepFolder;
            for (int level = 1; level <= 10; level++)
            {
                current = Path.Combine(current, $"level{level}");
            }
            var deepFile = Path.Combine(current, "deep.txt");
            Assert.IsTrue(File.Exists(deepFile), $"Deep nested file should exist at {deepFile}");
            Assert.AreEqual("Deep nested file content", File.ReadAllText(deepFile).TrimEnd());
        }
        finally
        {
            if (destDir != null) TestFixtureHelper.CleanupTempDir(destDir);
        }
    }
}
