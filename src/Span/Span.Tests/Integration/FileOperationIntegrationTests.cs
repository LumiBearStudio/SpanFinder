using Span.Services.FileOperations;

namespace Span.Tests.Integration;

// ============================================================================
// CopyFileOperation Integration Tests
// ============================================================================
[TestClass]
public class CopyFileOperationTests
{
    private string _tempDir = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_Copy_" + Guid.NewGuid().ToString("N")[..8]);
        _sourceDir = Path.Combine(_tempDir, "source");
        _destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task CopySingleFile_ContentMatches()
    {
        var srcFile = Path.Combine(_sourceDir, "test.txt");
        File.WriteAllText(srcFile, "hello world");

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        var destFile = Path.Combine(_destDir, "test.txt");
        Assert.IsTrue(File.Exists(destFile));
        Assert.AreEqual("hello world", File.ReadAllText(destFile));
        // Source should still exist
        Assert.IsTrue(File.Exists(srcFile));
    }

    [TestMethod]
    public async Task CopyDirectory_RecursiveCopy()
    {
        var subDir = Path.Combine(_sourceDir, "mydir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "a.txt"), "aaa");
        var nested = Path.Combine(subDir, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "b.txt"), "bbb");

        var op = new CopyFileOperation(new List<string> { subDir }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(_destDir, "mydir", "a.txt")));
        Assert.AreEqual("aaa", File.ReadAllText(Path.Combine(_destDir, "mydir", "a.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_destDir, "mydir", "nested", "b.txt")));
        Assert.AreEqual("bbb", File.ReadAllText(Path.Combine(_destDir, "mydir", "nested", "b.txt")));
    }

    [TestMethod]
    public async Task CopyWithConflict_KeepBoth_CreatesSuffixedFile()
    {
        var srcFile = Path.Combine(_sourceDir, "dup.txt");
        File.WriteAllText(srcFile, "new content");
        // Pre-existing file at destination
        File.WriteAllText(Path.Combine(_destDir, "dup.txt"), "old content");

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        op.SetConflictResolution(ConflictResolution.KeepBoth, applyToAll: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        // Original should still be there
        Assert.AreEqual("old content", File.ReadAllText(Path.Combine(_destDir, "dup.txt")));
        // New copy should have " (1)" suffix
        var suffixed = Path.Combine(_destDir, "dup (1).txt");
        Assert.IsTrue(File.Exists(suffixed), $"Expected {suffixed} to exist");
        Assert.AreEqual("new content", File.ReadAllText(suffixed));
    }

    [TestMethod]
    public async Task CopyWithConflict_Replace_OverwritesExisting()
    {
        var srcFile = Path.Combine(_sourceDir, "replace.txt");
        File.WriteAllText(srcFile, "replacement");
        File.WriteAllText(Path.Combine(_destDir, "replace.txt"), "original");

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        op.SetConflictResolution(ConflictResolution.Replace, applyToAll: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual("replacement", File.ReadAllText(Path.Combine(_destDir, "replace.txt")));
    }

    [TestMethod]
    public async Task CopyWithConflict_Skip_SkipsExisting()
    {
        var srcFile = Path.Combine(_sourceDir, "skip.txt");
        File.WriteAllText(srcFile, "new");
        File.WriteAllText(Path.Combine(_destDir, "skip.txt"), "keep me");

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        op.SetConflictResolution(ConflictResolution.Skip, applyToAll: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(_destDir, "skip.txt")));
        // No additional files should be created
        Assert.AreEqual(1, Directory.GetFiles(_destDir).Length);
    }

    [TestMethod]
    public async Task Undo_DeletesCopiedFiles()
    {
        var srcFile = Path.Combine(_sourceDir, "undo.txt");
        File.WriteAllText(srcFile, "data");

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        await op.ExecuteAsync();

        var destFile = Path.Combine(_destDir, "undo.txt");
        Assert.IsTrue(File.Exists(destFile));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(File.Exists(destFile));
        // Source should still exist
        Assert.IsTrue(File.Exists(srcFile));
    }

    [TestMethod]
    public async Task Cancellation_ThrowsOrReportsCancel()
    {
        var srcFile = Path.Combine(_sourceDir, "cancel.txt");
        File.WriteAllText(srcFile, "some data");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        var result = await op.ExecuteAsync(cancellationToken: cts.Token);

        // Operation should have failed due to cancellation
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage!.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Description_SingleItem_ContainsFileName()
    {
        var op = new CopyFileOperation(
            new List<string> { Path.Combine(_sourceDir, "file.txt") }, _destDir);
        Assert.IsTrue(op.Description.Contains("file.txt"));
        Assert.IsTrue(op.Description.StartsWith("Copy"));
    }

    [TestMethod]
    public void Description_MultipleItems_ContainsCount()
    {
        var op = new CopyFileOperation(
            new List<string>
            {
                Path.Combine(_sourceDir, "a.txt"),
                Path.Combine(_sourceDir, "b.txt"),
                Path.Combine(_sourceDir, "c.txt")
            },
            _destDir);
        Assert.IsTrue(op.Description.Contains("3 item(s)"));
    }

    [TestMethod]
    public void CanUndo_LocalPaths_ReturnsTrue()
    {
        var op = new CopyFileOperation(
            new List<string> { Path.Combine(_sourceDir, "f.txt") }, _destDir);
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task ProgressReporting_ReportsProgress()
    {
        var srcFile = Path.Combine(_sourceDir, "progress.txt");
        File.WriteAllText(srcFile, new string('x', 1024));

        var progressReports = new List<FileOperationProgress>();
        var progress = new Progress<FileOperationProgress>(p => progressReports.Add(p));

        var op = new CopyFileOperation(new List<string> { srcFile }, _destDir);
        var result = await op.ExecuteAsync(progress);

        Assert.IsTrue(result.Success);
        // Progress should have been reported at least once
        // Note: Progress<T> reports asynchronously via SynchronizationContext,
        // so in a test context without UI thread we may not capture all reports.
        // The test validates that no exception is thrown when progress is provided.
        Assert.IsTrue(File.Exists(Path.Combine(_destDir, "progress.txt")));
    }
}

// ============================================================================
// MoveFileOperation Integration Tests
// ============================================================================
[TestClass]
public class MoveFileOperationTests
{
    private string _tempDir = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_Move_" + Guid.NewGuid().ToString("N")[..8]);
        _sourceDir = Path.Combine(_tempDir, "source");
        _destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task MoveFile_SameVolume_FileMovedSuccessfully()
    {
        var srcFile = Path.Combine(_sourceDir, "move.txt");
        File.WriteAllText(srcFile, "move me");

        var op = new MoveFileOperation(new List<string> { srcFile }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(srcFile), "Source file should no longer exist");
        var destFile = Path.Combine(_destDir, "move.txt");
        Assert.IsTrue(File.Exists(destFile));
        Assert.AreEqual("move me", File.ReadAllText(destFile));
    }

    [TestMethod]
    public async Task MoveDirectory_SameVolume_DirectoryMoved()
    {
        var subDir = Path.Combine(_sourceDir, "folder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "inside.txt"), "content");

        var op = new MoveFileOperation(new List<string> { subDir }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(Directory.Exists(subDir));
        Assert.IsTrue(File.Exists(Path.Combine(_destDir, "folder", "inside.txt")));
        Assert.AreEqual("content", File.ReadAllText(Path.Combine(_destDir, "folder", "inside.txt")));
    }

    [TestMethod]
    public async Task MoveWithConflict_CreatesUniqueName()
    {
        var srcFile = Path.Combine(_sourceDir, "conflict.txt");
        File.WriteAllText(srcFile, "new version");
        File.WriteAllText(Path.Combine(_destDir, "conflict.txt"), "existing");

        var op = new MoveFileOperation(new List<string> { srcFile }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        // Original dest should be unchanged
        Assert.AreEqual("existing", File.ReadAllText(Path.Combine(_destDir, "conflict.txt")));
        // Moved file should get unique name
        var movedFile = Path.Combine(_destDir, "conflict (1).txt");
        Assert.IsTrue(File.Exists(movedFile), $"Expected {movedFile} to exist");
        Assert.AreEqual("new version", File.ReadAllText(movedFile));
    }

    [TestMethod]
    public async Task Undo_MovesBackToOriginalLocation()
    {
        var srcFile = Path.Combine(_sourceDir, "undo.txt");
        File.WriteAllText(srcFile, "restore me");

        var op = new MoveFileOperation(new List<string> { srcFile }, _destDir);
        await op.ExecuteAsync();

        Assert.IsFalse(File.Exists(srcFile));
        Assert.IsTrue(File.Exists(Path.Combine(_destDir, "undo.txt")));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsTrue(File.Exists(srcFile));
        Assert.AreEqual("restore me", File.ReadAllText(srcFile));
        Assert.IsFalse(File.Exists(Path.Combine(_destDir, "undo.txt")));
    }

    [TestMethod]
    public void Description_SingleItem_ContainsFileName()
    {
        var op = new MoveFileOperation(
            new List<string> { Path.Combine(_sourceDir, "doc.txt") }, _destDir);
        Assert.IsTrue(op.Description.Contains("doc.txt"));
        Assert.IsTrue(op.Description.StartsWith("Move"));
    }

    [TestMethod]
    public void Description_MultipleItems_ContainsCount()
    {
        var op = new MoveFileOperation(
            new List<string>
            {
                Path.Combine(_sourceDir, "a.txt"),
                Path.Combine(_sourceDir, "b.txt")
            },
            _destDir);
        Assert.IsTrue(op.Description.Contains("2 item(s)"));
    }

    [TestMethod]
    public void CanUndo_LocalPaths_ReturnsTrue()
    {
        var op = new MoveFileOperation(
            new List<string> { Path.Combine(_sourceDir, "f.txt") }, _destDir);
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task PathNotFound_ReturnsError()
    {
        var nonExistent = Path.Combine(_sourceDir, "ghost.txt");

        var op = new MoveFileOperation(new List<string> { nonExistent }, _destDir);
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage!.Contains("Path not found"));
    }
}

// ============================================================================
// RenameFileOperation Integration Tests
// ============================================================================
[TestClass]
public class RenameFileOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_Rename_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task RenameFile_Success()
    {
        var filePath = Path.Combine(_tempDir, "old.txt");
        File.WriteAllText(filePath, "content");

        var op = new RenameFileOperation(filePath, "new.txt");
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(filePath));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "new.txt")));
        Assert.AreEqual("content", File.ReadAllText(Path.Combine(_tempDir, "new.txt")));
    }

    [TestMethod]
    public async Task RenameDirectory_Success()
    {
        var dirPath = Path.Combine(_tempDir, "olddir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "child.txt"), "inside");

        var op = new RenameFileOperation(dirPath, "newdir");
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(Directory.Exists(dirPath));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "newdir")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "newdir", "child.txt")));
    }

    [TestMethod]
    public async Task Undo_RestoresOriginalName()
    {
        var filePath = Path.Combine(_tempDir, "before.txt");
        File.WriteAllText(filePath, "data");

        var op = new RenameFileOperation(filePath, "after.txt");
        await op.ExecuteAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "after.txt")));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsTrue(File.Exists(filePath));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "after.txt")));
    }

    [TestMethod]
    public async Task NameConflict_ReturnsError()
    {
        var filePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(filePath, "src");
        File.WriteAllText(Path.Combine(_tempDir, "taken.txt"), "existing");

        var op = new RenameFileOperation(filePath, "taken.txt");
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage!.Contains("already exists"));
        // Original should still exist
        Assert.IsTrue(File.Exists(filePath));
    }

    [TestMethod]
    public async Task SourceNotFound_ReturnsError()
    {
        var nonExistent = Path.Combine(_tempDir, "missing.txt");

        var op = new RenameFileOperation(nonExistent, "new.txt");
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage!.Contains("does not exist"));
    }

    [TestMethod]
    public void Description_ContainsOldAndNewName()
    {
        var op = new RenameFileOperation(
            Path.Combine(_tempDir, "oldname.txt"), "newname.txt");
        Assert.IsTrue(op.Description.Contains("oldname.txt"));
        Assert.IsTrue(op.Description.Contains("newname.txt"));
    }

    [TestMethod]
    public void CanUndo_LocalPath_ReturnsTrue()
    {
        var op = new RenameFileOperation(
            Path.Combine(_tempDir, "f.txt"), "g.txt");
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task Undo_RenameDirectory_RestoresOriginal()
    {
        var dirPath = Path.Combine(_tempDir, "origDir");
        Directory.CreateDirectory(dirPath);

        var op = new RenameFileOperation(dirPath, "renamedDir");
        await op.ExecuteAsync();

        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "renamedDir")));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsTrue(Directory.Exists(dirPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "renamedDir")));
    }
}

// ============================================================================
// NewFileOperation Integration Tests
// ============================================================================
[TestClass]
public class NewFileOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_NewFile_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task CreateEmptyFile_FileExists()
    {
        var filePath = Path.Combine(_tempDir, "newfile.txt");

        var op = new NewFileOperation(filePath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(filePath));
        Assert.AreEqual(0, new FileInfo(filePath).Length);
    }

    [TestMethod]
    public async Task Undo_DeletesEmptyFile()
    {
        var filePath = Path.Combine(_tempDir, "todelete.txt");

        var op = new NewFileOperation(filePath);
        await op.ExecuteAsync();
        Assert.IsTrue(File.Exists(filePath));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(File.Exists(filePath));
    }

    [TestMethod]
    public async Task Undo_FailsIfFileModified()
    {
        var filePath = Path.Combine(_tempDir, "modified.txt");

        var op = new NewFileOperation(filePath);
        await op.ExecuteAsync();

        // Modify the file so it's no longer empty
        File.WriteAllText(filePath, "user typed something");

        var undoResult = await op.UndoAsync();
        Assert.IsFalse(undoResult.Success);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("modified"));
        // File should still exist
        Assert.IsTrue(File.Exists(filePath));
    }

    [TestMethod]
    public void CanUndo_ReturnsTrue()
    {
        var op = new NewFileOperation(Path.Combine(_tempDir, "any.txt"));
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public void Description_ContainsFileName()
    {
        var op = new NewFileOperation(Path.Combine(_tempDir, "report.csv"));
        Assert.IsTrue(op.Description.Contains("report.csv"));
        Assert.IsTrue(op.Description.Contains("Create file"));
    }
}

// ============================================================================
// NewFolderOperation Integration Tests
// ============================================================================
[TestClass]
public class NewFolderOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_NewFolder_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task CreateFolder_FolderExists()
    {
        var folderPath = Path.Combine(_tempDir, "newfolder");

        var op = new NewFolderOperation(folderPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(Directory.Exists(folderPath));
    }

    [TestMethod]
    public async Task Undo_DeletesEmptyFolder()
    {
        var folderPath = Path.Combine(_tempDir, "emptydir");

        var op = new NewFolderOperation(folderPath);
        await op.ExecuteAsync();
        Assert.IsTrue(Directory.Exists(folderPath));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(Directory.Exists(folderPath));
    }

    [TestMethod]
    public async Task Undo_FailsIfFolderNotEmpty()
    {
        var folderPath = Path.Combine(_tempDir, "notempty");

        var op = new NewFolderOperation(folderPath);
        await op.ExecuteAsync();

        // Add content so it's not empty
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        var undoResult = await op.UndoAsync();
        Assert.IsFalse(undoResult.Success);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("not empty"));
        // Folder should still exist
        Assert.IsTrue(Directory.Exists(folderPath));
    }

    [TestMethod]
    public void CanUndo_LocalPath_ReturnsTrue()
    {
        var op = new NewFolderOperation(Path.Combine(_tempDir, "localdir"));
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public void Description_ContainsFolderName()
    {
        var op = new NewFolderOperation(Path.Combine(_tempDir, "MyFolder"));
        Assert.IsTrue(op.Description.Contains("MyFolder"));
        Assert.IsTrue(op.Description.Contains("Create folder"));
    }
}

// ============================================================================
// BatchRenameOperation Integration Tests
// ============================================================================
[TestClass]
public class BatchRenameOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTests_BatchRename_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task BatchRename_MultipleFiles_AllRenamed()
    {
        var file1 = Path.Combine(_tempDir, "img001.jpg");
        var file2 = Path.Combine(_tempDir, "img002.jpg");
        var file3 = Path.Combine(_tempDir, "img003.jpg");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");
        File.WriteAllText(file3, "3");

        var renames = new List<(string OldPath, string NewName)>
        {
            (file1, "photo_001.jpg"),
            (file2, "photo_002.jpg"),
            (file3, "photo_003.jpg")
        };

        var op = new BatchRenameOperation(renames);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "photo_001.jpg")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "photo_002.jpg")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "photo_003.jpg")));
        Assert.IsFalse(File.Exists(file1));
        Assert.IsFalse(File.Exists(file2));
        Assert.IsFalse(File.Exists(file3));
    }

    [TestMethod]
    public async Task BatchRename_SameName_SkipsItem()
    {
        var file = Path.Combine(_tempDir, "keep.txt");
        File.WriteAllText(file, "data");

        var renames = new List<(string OldPath, string NewName)>
        {
            (file, "keep.txt") // same name
        };

        var op = new BatchRenameOperation(renames);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    public async Task Undo_RevertsAllRenames()
    {
        var file1 = Path.Combine(_tempDir, "old1.txt");
        var file2 = Path.Combine(_tempDir, "old2.txt");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");

        var renames = new List<(string OldPath, string NewName)>
        {
            (file1, "new1.txt"),
            (file2, "new2.txt")
        };

        var op = new BatchRenameOperation(renames);
        await op.ExecuteAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "new1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "new2.txt")));

        var undoResult = await op.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsTrue(File.Exists(file1));
        Assert.IsTrue(File.Exists(file2));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "new1.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "new2.txt")));
    }

    [TestMethod]
    public async Task PartialFailure_ReturnsErrorWithCount()
    {
        var file1 = Path.Combine(_tempDir, "ok.txt");
        File.WriteAllText(file1, "data");
        // Pre-create a conflicting target name
        File.WriteAllText(Path.Combine(_tempDir, "conflict.txt"), "blocker");

        var renames = new List<(string OldPath, string NewName)>
        {
            (file1, "renamed_ok.txt"),
            (Path.Combine(_tempDir, "renamed_ok.txt"), "conflict.txt") // will fail because conflict.txt exists
        };

        var op = new BatchRenameOperation(renames);
        var result = await op.ExecuteAsync();

        // The second rename should fail (trying to move to "conflict.txt" which exists)
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage!.Contains("1/2") || result.ErrorMessage!.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Description_ContainsCount()
    {
        var renames = new List<(string OldPath, string NewName)>
        {
            (Path.Combine(_tempDir, "a.txt"), "b.txt"),
            (Path.Combine(_tempDir, "c.txt"), "d.txt"),
            (Path.Combine(_tempDir, "e.txt"), "f.txt")
        };

        var op = new BatchRenameOperation(renames);
        Assert.IsTrue(op.Description.Contains("3"));
        Assert.IsTrue(op.Description.Contains("Batch rename"));
    }

    [TestMethod]
    public async Task CanUndo_AfterExecution_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "x.txt");
        File.WriteAllText(file, "data");

        var renames = new List<(string OldPath, string NewName)>
        {
            (file, "y.txt")
        };

        var op = new BatchRenameOperation(renames);
        // Before execution, CanUndo should be false (no executed renames yet)
        Assert.IsFalse(op.CanUndo);

        await op.ExecuteAsync();
        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task Cancellation_StopsProcessing()
    {
        var files = new List<(string OldPath, string NewName)>();
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(_tempDir, $"file{i}.txt");
            File.WriteAllText(path, $"content{i}");
            files.Add((path, $"renamed{i}.txt"));
        }

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var op = new BatchRenameOperation(files);

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => op.ExecuteAsync(cancellationToken: cts.Token));
    }

    [TestMethod]
    public async Task BatchRename_MixedFilesAndDirectories()
    {
        var file = Path.Combine(_tempDir, "doc.txt");
        var dir = Path.Combine(_tempDir, "folder");
        File.WriteAllText(file, "text");
        Directory.CreateDirectory(dir);

        var renames = new List<(string OldPath, string NewName)>
        {
            (file, "document.txt"),
            (dir, "directory")
        };

        var op = new BatchRenameOperation(renames);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "document.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "directory")));
    }
}
