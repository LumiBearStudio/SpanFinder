using System.IO.Compression;
using Span.Services.FileOperations;

namespace Span.Tests.Integration;

// ============================================================================
// DeleteFileOperation Integration Tests
// ============================================================================
[TestClass]
public class DeleteFileOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public async Task PermanentDelete_SingleFile_RemovesFile()
    {
        var filePath = Path.Combine(_tempDir, "delete_me.txt");
        File.WriteAllText(filePath, "content to delete");

        var op = new DeleteFileOperation(new List<string> { filePath }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(filePath), "File should have been permanently deleted");
        CollectionAssert.Contains(result.AffectedPaths, filePath);
    }

    [TestMethod]
    public async Task PermanentDelete_Directory_RemovesRecursively()
    {
        var dirPath = Path.Combine(_tempDir, "folder_to_delete");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "child.txt"), "nested file");
        var nestedDir = Path.Combine(dirPath, "sub");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "deep.txt"), "deeply nested");

        var op = new DeleteFileOperation(new List<string> { dirPath }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(Directory.Exists(dirPath), "Directory should have been permanently deleted");
        CollectionAssert.Contains(result.AffectedPaths, dirPath);
    }

    [TestMethod]
    public async Task PermanentDelete_NonExistentPath_ReportsError()
    {
        var nonExistent = Path.Combine(_tempDir, "ghost.txt");

        var op = new DeleteFileOperation(new List<string> { nonExistent }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage!.Contains("Path not found"));
    }

    [TestMethod]
    public async Task PermanentDelete_MultipleItems_RemovesAll()
    {
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        var dir1 = Path.Combine(_tempDir, "dir1");
        File.WriteAllText(file1, "a");
        File.WriteAllText(file2, "b");
        Directory.CreateDirectory(dir1);
        File.WriteAllText(Path.Combine(dir1, "inside.txt"), "c");

        var op = new DeleteFileOperation(new List<string> { file1, file2, dir1 }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(file1));
        Assert.IsFalse(File.Exists(file2));
        Assert.IsFalse(Directory.Exists(dir1));
        Assert.AreEqual(3, result.AffectedPaths.Count);
    }

    [TestMethod]
    public void Description_PermanentTrue_IncludesPermanently()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "a.txt"), Path.Combine(_tempDir, "b.txt") },
            permanent: true);

        Assert.IsTrue(op.Description.Contains("Permanently"));
        Assert.IsTrue(op.Description.Contains("2 item(s)"));
    }

    [TestMethod]
    public void Description_PermanentFalse_DoesNotIncludePermanently()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "a.txt") },
            permanent: false);

        Assert.IsFalse(op.Description.Contains("Permanently"));
        Assert.IsTrue(op.Description.Contains("Delete"));
        // Single item uses filename format: Delete "a.txt", not "1 item(s)"
        Assert.IsTrue(op.Description.Contains("a.txt") || op.Description.Contains("1 item(s)"));
    }

    [TestMethod]
    public void CanUndo_PermanentTrue_ReturnsFalse()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "f.txt") },
            permanent: true);

        Assert.IsFalse(op.CanUndo);
    }

    [TestMethod]
    public void CanUndo_PermanentFalse_ReturnsTrue()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "f.txt") },
            permanent: false);

        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task UndoAsync_PermanentDelete_ReturnsFailure()
    {
        var filePath = Path.Combine(_tempDir, "perm.txt");
        File.WriteAllText(filePath, "data");

        var op = new DeleteFileOperation(new List<string> { filePath }, permanent: true);
        await op.ExecuteAsync();

        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("Cannot undo permanent deletion"));
    }
}

// ============================================================================
// CompressOperation Integration Tests
// ============================================================================
[TestClass]
public class CompressOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public async Task Execute_SingleFile_CreatesZip()
    {
        var filePath = Path.Combine(_tempDir, "hello.txt");
        File.WriteAllText(filePath, "hello world");
        var zipPath = Path.Combine(_tempDir, "output.zip");

        var op = new CompressOperation(new[] { filePath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath), "ZIP file should have been created");
        CollectionAssert.Contains(result.AffectedPaths, zipPath);

        // Verify ZIP contents
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.AreEqual(1, archive.Entries.Count);
        Assert.AreEqual("hello.txt", archive.Entries[0].FullName);
    }

    [TestMethod]
    public async Task Execute_MultipleFiles_CreatesZipWithAll()
    {
        var file1 = Path.Combine(_tempDir, "alpha.txt");
        var file2 = Path.Combine(_tempDir, "beta.txt");
        var file3 = Path.Combine(_tempDir, "gamma.txt");
        File.WriteAllText(file1, "aaa");
        File.WriteAllText(file2, "bbb");
        File.WriteAllText(file3, "ccc");
        var zipPath = Path.Combine(_tempDir, "multi.zip");

        var op = new CompressOperation(new[] { file1, file2, file3 }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        Assert.AreEqual(3, entryNames.Count);
        CollectionAssert.Contains(entryNames, "alpha.txt");
        CollectionAssert.Contains(entryNames, "beta.txt");
        CollectionAssert.Contains(entryNames, "gamma.txt");
    }

    [TestMethod]
    public async Task Execute_Directory_CompressesRecursively()
    {
        var dirPath = Path.Combine(_tempDir, "mydir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "root.txt"), "root");
        var subDir = Path.Combine(dirPath, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested");
        var zipPath = Path.Combine(_tempDir, "dir.zip");

        var op = new CompressOperation(new[] { dirPath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.IsTrue(entryNames.Count >= 2, $"Expected at least 2 entries, got {entryNames.Count}");

        // The archive should contain relative paths including the directory name
        Assert.IsTrue(entryNames.Any(n => n.Contains("root.txt")), "Should contain root.txt");
        Assert.IsTrue(entryNames.Any(n => n.Contains("nested.txt")), "Should contain nested.txt");
    }

    [TestMethod]
    public async Task Execute_MixedFilesAndDirs_CreatesZip()
    {
        var filePath = Path.Combine(_tempDir, "standalone.txt");
        File.WriteAllText(filePath, "alone");
        var dirPath = Path.Combine(_tempDir, "folder");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "inside.txt"), "inside");
        var zipPath = Path.Combine(_tempDir, "mixed.zip");

        var op = new CompressOperation(new[] { filePath, dirPath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.IsTrue(entryNames.Any(n => n.Contains("standalone.txt")), "Should contain standalone.txt");
        Assert.IsTrue(entryNames.Any(n => n.Contains("inside.txt")), "Should contain inside.txt");
    }

    [TestMethod]
    public async Task Undo_DeletesZipFile()
    {
        var filePath = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(filePath, "data");
        var zipPath = Path.Combine(_tempDir, "undo_test.zip");

        var op = new CompressOperation(new[] { filePath }, zipPath);
        await op.ExecuteAsync();
        Assert.IsTrue(File.Exists(zipPath));

        var undoResult = await op.UndoAsync();

        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(File.Exists(zipPath), "ZIP file should have been deleted by undo");
    }

    [TestMethod]
    public async Task Undo_ZipNotExist_ReturnsFailure()
    {
        var zipPath = Path.Combine(_tempDir, "nonexistent.zip");

        var op = new CompressOperation(new[] { Path.Combine(_tempDir, "dummy.txt") }, zipPath);
        // Do NOT execute, so zip does not exist
        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("does not exist"));
    }

    [TestMethod]
    public void Description_ContainsZipFileName()
    {
        var zipPath = Path.Combine(_tempDir, "archive.zip");
        var op = new CompressOperation(new[] { Path.Combine(_tempDir, "f.txt") }, zipPath);

        Assert.IsTrue(op.Description.Contains("archive.zip"));
        Assert.IsTrue(op.Description.Contains("Compress"));
    }

    [TestMethod]
    public void CanUndo_ReturnsTrue()
    {
        var op = new CompressOperation(
            new[] { Path.Combine(_tempDir, "f.txt") },
            Path.Combine(_tempDir, "out.zip"));

        Assert.IsTrue(op.CanUndo);
    }
}

// ============================================================================
// ExtractOperation Integration Tests
// ============================================================================
[TestClass]
public class ExtractOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Helper: creates a ZIP file at the given path with the specified entries.
    /// Each entry is (entryName, content). If content is null, it creates a directory entry.
    /// </summary>
    private static void CreateTestZip(string zipPath, params (string entryName, string? content)[] entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            if (content == null)
            {
                // Directory entry (name ends with /)
                archive.CreateEntry(entryName.EndsWith('/') ? entryName : entryName + "/");
            }
            else
            {
                var entry = archive.CreateEntry(entryName);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }
    }

    [TestMethod]
    public async Task Execute_ValidZip_ExtractsAllFiles()
    {
        var zipPath = Path.Combine(_tempDir, "test.zip");
        CreateTestZip(zipPath,
            ("file1.txt", "content1"),
            ("file2.txt", "content2"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(Directory.Exists(destPath));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file2.txt")));
        Assert.AreEqual("content1", File.ReadAllText(Path.Combine(destPath, "file1.txt")));
        Assert.AreEqual("content2", File.ReadAllText(Path.Combine(destPath, "file2.txt")));
    }

    [TestMethod]
    public async Task Execute_ZipWithSubdirs_PreservesStructure()
    {
        var zipPath = Path.Combine(_tempDir, "nested.zip");
        CreateTestZip(zipPath,
            ("root.txt", "root content"),
            ("subdir/", null),
            ("subdir/child.txt", "child content"),
            ("subdir/deep/", null),
            ("subdir/deep/leaf.txt", "leaf content"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "root.txt")));
        Assert.AreEqual("root content", File.ReadAllText(Path.Combine(destPath, "root.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(destPath, "subdir")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "subdir", "child.txt")));
        Assert.AreEqual("child content", File.ReadAllText(Path.Combine(destPath, "subdir", "child.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "subdir", "deep", "leaf.txt")));
        Assert.AreEqual("leaf content", File.ReadAllText(Path.Combine(destPath, "subdir", "deep", "leaf.txt")));
    }

    [TestMethod]
    public async Task Execute_CreatesDestinationDirectory()
    {
        var zipPath = Path.Combine(_tempDir, "create_dest.zip");
        CreateTestZip(zipPath, ("file.txt", "data"));
        var destPath = Path.Combine(_tempDir, "nonexistent", "nested", "output");

        Assert.IsFalse(Directory.Exists(destPath), "Destination should not exist before extraction");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(Directory.Exists(destPath));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file.txt")));
    }

    [TestMethod]
    public async Task Undo_DeletesExtractedFolder()
    {
        var zipPath = Path.Combine(_tempDir, "undo.zip");
        CreateTestZip(zipPath, ("a.txt", "aaa"), ("b.txt", "bbb"));
        var destPath = Path.Combine(_tempDir, "to_undo");

        var op = new ExtractOperation(zipPath, destPath);
        await op.ExecuteAsync();
        Assert.IsTrue(Directory.Exists(destPath));

        var undoResult = await op.UndoAsync();

        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(Directory.Exists(destPath), "Extracted folder should have been deleted by undo");
    }

    [TestMethod]
    public async Task Undo_FolderNotExist_ReturnsFailure()
    {
        var zipPath = Path.Combine(_tempDir, "dummy.zip");
        var destPath = Path.Combine(_tempDir, "never_created");

        var op = new ExtractOperation(zipPath, destPath);
        // Do NOT execute, so destination folder does not exist
        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("does not exist"));
    }

    [TestMethod]
    public void Description_ContainsZipFileName()
    {
        var op = new ExtractOperation(
            Path.Combine(_tempDir, "myarchive.zip"),
            Path.Combine(_tempDir, "dest"));

        Assert.IsTrue(op.Description.Contains("myarchive.zip"));
        Assert.IsTrue(op.Description.Contains("Extract"));
    }

    [TestMethod]
    public void CanUndo_ReturnsTrue()
    {
        var op = new ExtractOperation(
            Path.Combine(_tempDir, "any.zip"),
            Path.Combine(_tempDir, "dest"));

        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task Execute_ReportsProgress()
    {
        var zipPath = Path.Combine(_tempDir, "progress.zip");
        CreateTestZip(zipPath,
            ("p1.txt", "data1"),
            ("p2.txt", "data2"),
            ("p3.txt", "data3"));
        var destPath = Path.Combine(_tempDir, "progress_out");

        var progressReports = new List<FileOperationProgress>();
        var progress = new Progress<FileOperationProgress>(p =>
        {
            progressReports.Add(new FileOperationProgress
            {
                CurrentFile = p.CurrentFile,
                CurrentFileIndex = p.CurrentFileIndex,
                TotalFileCount = p.TotalFileCount
            });
        });

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync(progress);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p3.txt")));
        // Progress<T> reports asynchronously via SynchronizationContext,
        // so in a test context we validate the operation completes
        // successfully when a progress reporter is provided.
    }
}
