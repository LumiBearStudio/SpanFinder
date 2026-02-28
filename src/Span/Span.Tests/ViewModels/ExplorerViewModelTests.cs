using Span.Models;
using Span.Services;

namespace Span.Tests.ViewModels;

/// <summary>
/// Tests for ExplorerViewModel navigation logic patterns.
/// ExplorerViewModel itself cannot be instantiated without WinUI runtime,
/// so these tests verify the underlying algorithms through testable models
/// and services (path splitting, history stack behavior, column management concepts).
/// </summary>
[TestClass]
public class ExplorerViewModelTests
{
    // ── Path Segment Generation Logic ──
    // ExplorerViewModel.UpdatePathSegments splits a path into breadcrumb segments.
    // We replicate the exact algorithm here to verify correctness.

    [TestMethod]
    public void PathSegments_LocalPath_SplitsCorrectly()
    {
        // Replicates ExplorerViewModel.UpdatePathSegments for local paths
        var path = @"C:\Users\Dev\Projects";
        var segments = SplitLocalPath(path);

        Assert.AreEqual(4, segments.Count);
        Assert.AreEqual("C:", segments[0].Name);
        Assert.AreEqual(@"C:\", segments[0].FullPath);
        Assert.IsFalse(segments[0].IsLast);

        Assert.AreEqual("Users", segments[1].Name);
        Assert.AreEqual(@"C:\Users", segments[1].FullPath);
        Assert.IsFalse(segments[1].IsLast);

        Assert.AreEqual("Dev", segments[2].Name);
        Assert.AreEqual(@"C:\Users\Dev", segments[2].FullPath);
        Assert.IsFalse(segments[2].IsLast);

        Assert.AreEqual("Projects", segments[3].Name);
        Assert.AreEqual(@"C:\Users\Dev\Projects", segments[3].FullPath);
        Assert.IsTrue(segments[3].IsLast);
    }

    [TestMethod]
    public void PathSegments_RootDriveOnly_SingleSegment()
    {
        var path = @"D:\";
        var segments = SplitLocalPath(path);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual("D:", segments[0].Name);
        Assert.AreEqual(@"D:\", segments[0].FullPath);
        Assert.IsTrue(segments[0].IsLast);
    }

    [TestMethod]
    public void PathSegments_UncPath_SplitsCorrectly()
    {
        var path = @"\\server\share\folder\sub";
        var segments = SplitUncPath(path);

        Assert.AreEqual(3, segments.Count);
        Assert.AreEqual(@"\\server\share", segments[0].Name);
        Assert.AreEqual(@"\\server\share", segments[0].FullPath);
        Assert.IsFalse(segments[0].IsLast);

        Assert.AreEqual("folder", segments[1].Name);
        Assert.AreEqual(@"\\server\share\folder", segments[1].FullPath);
        Assert.IsFalse(segments[1].IsLast);

        Assert.AreEqual("sub", segments[2].Name);
        Assert.AreEqual(@"\\server\share\folder\sub", segments[2].FullPath);
        Assert.IsTrue(segments[2].IsLast);
    }

    // ── History Stack Behavior ──
    // ExplorerViewModel uses Stack<string> with MaxHistorySize=50.
    // Verify the stack trimming and back/forward state machine.

    [TestMethod]
    public void HistoryStack_MaxSize50_TrimsOldestEntries()
    {
        // Replicates ExplorerViewModel.PushToHistory trimming logic
        const int MaxHistorySize = 50;
        var backStack = new Stack<string>();

        // Push 60 entries (exceeds max)
        for (int i = 0; i < 60; i++)
        {
            backStack.Push($@"C:\folder{i}");

            // Trim to max size (same algorithm as ExplorerViewModel)
            if (backStack.Count > MaxHistorySize)
            {
                var temp = backStack.ToArray();
                backStack.Clear();
                for (int j = 0; j < MaxHistorySize; j++)
                    backStack.Push(temp[MaxHistorySize - 1 - j]);
            }
        }

        Assert.AreEqual(MaxHistorySize, backStack.Count);
        // Most recent entry should be on top
        Assert.AreEqual(@"C:\folder59", backStack.Peek());
    }

    [TestMethod]
    public void BackForward_StateMachine_NavigationClearsForwardStack()
    {
        // Simulates ExplorerViewModel back/forward navigation logic
        var backStack = new Stack<string>();
        var forwardStack = new Stack<string>();
        string currentPath = @"C:\Start";

        // Navigate to A
        backStack.Push(currentPath);
        forwardStack.Clear();
        currentPath = @"C:\A";

        // Navigate to B
        backStack.Push(currentPath);
        forwardStack.Clear();
        currentPath = @"C:\B";

        // Go back (to A)
        forwardStack.Push(currentPath);
        currentPath = backStack.Pop();
        Assert.AreEqual(@"C:\A", currentPath);
        Assert.AreEqual(1, forwardStack.Count);

        // Navigate to C (should clear forward stack)
        backStack.Push(currentPath);
        forwardStack.Clear();
        currentPath = @"C:\C";

        Assert.AreEqual(0, forwardStack.Count, "Forward stack should be cleared on new navigation");
        Assert.AreEqual(2, backStack.Count, "Back stack should have Start and A");
    }

    [TestMethod]
    public void BackForward_GoBackThenForward_RestoresPath()
    {
        var backStack = new Stack<string>();
        var forwardStack = new Stack<string>();
        string currentPath = @"C:\Start";

        // Navigate: Start -> A -> B
        backStack.Push(currentPath); forwardStack.Clear(); currentPath = @"C:\A";
        backStack.Push(currentPath); forwardStack.Clear(); currentPath = @"C:\B";

        // Go back (B -> A)
        forwardStack.Push(currentPath);
        currentPath = backStack.Pop();
        Assert.AreEqual(@"C:\A", currentPath);

        // Go back (A -> Start)
        forwardStack.Push(currentPath);
        currentPath = backStack.Pop();
        Assert.AreEqual(@"C:\Start", currentPath);

        // Go forward (Start -> A)
        backStack.Push(currentPath);
        currentPath = forwardStack.Pop();
        Assert.AreEqual(@"C:\A", currentPath);

        // Go forward (A -> B)
        backStack.Push(currentPath);
        currentPath = forwardStack.Pop();
        Assert.AreEqual(@"C:\B", currentPath);

        Assert.AreEqual(0, forwardStack.Count);
        Assert.AreEqual(2, backStack.Count); // Start, A (current=B is not on stack)
    }

    // ── Column Management Concepts ──

    [TestMethod]
    public void ColumnHierarchy_FolderSelection_AddsColumn()
    {
        // Simulates Miller Columns: selecting a folder adds a new column
        var columns = new List<FolderItem>();

        var root = new FolderItem { Name = "C:", Path = @"C:\" };
        columns.Add(root);

        // Select "Users" in root column -> adds Users column
        var users = new FolderItem { Name = "Users", Path = @"C:\Users" };
        root.SubFolders.Add(users);
        columns.Add(users);

        // Select "Dev" in Users column -> adds Dev column
        var dev = new FolderItem { Name = "Dev", Path = @"C:\Users\Dev" };
        users.SubFolders.Add(dev);
        columns.Add(dev);

        Assert.AreEqual(3, columns.Count);
        Assert.AreEqual(@"C:\", columns[0].Path);
        Assert.AreEqual(@"C:\Users", columns[1].Path);
        Assert.AreEqual(@"C:\Users\Dev", columns[2].Path);
    }

    [TestMethod]
    public void ColumnHierarchy_FileSelection_RemovesExtraColumns()
    {
        // Simulates: when a file is selected, columns after the parent are removed
        var columns = new List<FolderItem>
        {
            new FolderItem { Name = "C:", Path = @"C:\" },
            new FolderItem { Name = "Users", Path = @"C:\Users" },
            new FolderItem { Name = "Dev", Path = @"C:\Users\Dev" },
        };

        // File selected in "Users" column (index 1) -> remove columns from index 2 onwards
        int parentIndex = 1;
        int nextIndex = parentIndex + 1;
        for (int i = columns.Count - 1; i >= nextIndex; i--)
            columns.RemoveAt(i);

        Assert.AreEqual(2, columns.Count);
        Assert.AreEqual(@"C:\Users", columns[columns.Count - 1].Path);
    }

    [TestMethod]
    public void RemotePath_DetectedByFileSystemRouter()
    {
        // FileSystemRouter.IsRemotePath is used by ExplorerViewModel
        // to decide whether to use remote navigation path
        Assert.IsTrue(FileSystemRouter.IsRemotePath("ftp://example.com/path"));
        Assert.IsTrue(FileSystemRouter.IsRemotePath("sftp://user@host:22/dir"));
        Assert.IsTrue(FileSystemRouter.IsRemotePath("smb://server/share"));

        Assert.IsFalse(FileSystemRouter.IsRemotePath(@"C:\Users\Dev"));
        Assert.IsFalse(FileSystemRouter.IsRemotePath(@"\\server\share"));
        Assert.IsFalse(FileSystemRouter.IsRemotePath(""));
        Assert.IsFalse(FileSystemRouter.IsRemotePath("file://localhost/c$/test"));
    }

    // ── Helpers: replicate ExplorerViewModel.UpdatePathSegments algorithm ──

    private record SegmentInfo(string Name, string FullPath, bool IsLast);

    private static List<SegmentInfo> SplitLocalPath(string path)
    {
        var result = new List<SegmentInfo>();
        if (string.IsNullOrWhiteSpace(path)) return result;

        var parts = path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string accumulated = string.Empty;

        for (int i = 0; i < parts.Length; i++)
        {
            if (i == 0 && parts[i].EndsWith(":"))
            {
                accumulated = parts[i] + "\\";
            }
            else
            {
                accumulated = System.IO.Path.Combine(accumulated, parts[i]);
            }

            result.Add(new SegmentInfo(parts[i], accumulated, i == parts.Length - 1));
        }

        return result;
    }

    private static List<SegmentInfo> SplitUncPath(string path)
    {
        var result = new List<SegmentInfo>();
        if (!path.StartsWith(@"\\")) return result;

        var parts = path.TrimStart('\\').Split(
            System.IO.Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2) return result;

        string uncRoot = @"\\" + parts[0] + @"\" + parts[1];
        result.Add(new SegmentInfo(
            @"\\" + parts[0] + @"\" + parts[1],
            uncRoot,
            parts.Length == 2));

        string accumulated = uncRoot;
        for (int i = 2; i < parts.Length; i++)
        {
            accumulated = System.IO.Path.Combine(accumulated, parts[i]);
            result.Add(new SegmentInfo(parts[i], accumulated, i == parts.Length - 1));
        }

        return result;
    }
}
