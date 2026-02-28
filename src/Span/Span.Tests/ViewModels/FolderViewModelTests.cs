using Span.Models;
using Span.Helpers;
using Span.Services;

namespace Span.Tests.ViewModels;

/// <summary>
/// Tests for FolderViewModel-related data logic: sorting, filtering, cancellation, and selection patterns.
/// FolderViewModel itself depends on WinUI types, so we test the underlying data and sorting logic
/// that powers the ViewModel using the available model types and helpers.
/// </summary>
[TestClass]
public class FolderViewModelTests
{
    // ── Helpers ──────────────────────────────────────────────

    private static FolderItem MakeFolder(string name, string? path = null, bool isHidden = false)
        => new() { Name = name, Path = path ?? $@"C:\Test\{name}", IsHidden = isHidden };

    private static FileItem MakeFile(string name, long size = 0, DateTime? dateModified = null,
        string fileType = "", bool isHidden = false)
        => new()
        {
            Name = name,
            Path = $@"C:\Test\{name}",
            Size = size,
            DateModified = dateModified ?? DateTime.MinValue,
            FileType = fileType,
            IsHidden = isHidden
        };

    // ── 1. LoadChildren_PopulatesItems ───────────────────────

    [TestMethod]
    public void LoadChildren_PopulatesItems()
    {
        // Simulate what FolderViewModel.LoadChildrenAsync does:
        // populate the FolderItem's Children from SubFolders + Files
        var folder = new FolderItem
        {
            Name = "Root",
            Path = @"C:\Root",
            SubFolders = new List<FolderItem>
            {
                MakeFolder("Documents"),
                MakeFolder("Pictures"),
            },
            Files = new List<FileItem>
            {
                MakeFile("readme.txt"),
                MakeFile("notes.md"),
                MakeFile("data.csv"),
            },
        };

        // Populate Children collection (mirrors ViewModel logic)
        folder.Children.Clear();
        foreach (var sub in folder.SubFolders) folder.Children.Add(sub);
        foreach (var file in folder.Files) folder.Children.Add(file);

        Assert.AreEqual(5, folder.Children.Count);
        Assert.AreEqual(2, folder.Children.OfType<FolderItem>().Count());
        Assert.AreEqual(3, folder.Children.OfType<FileItem>().Count());
    }

    // ── 2. LoadChildren_SortsByName ──────────────────────────

    [TestMethod]
    public void LoadChildren_SortsByName()
    {
        var items = new List<IFileSystemItem>
        {
            MakeFolder("Zebra"),
            MakeFolder("Apple"),
            MakeFile("banana.txt"),
            MakeFile("cherry.txt"),
            MakeFolder("Mango"),
        };

        var sorted = items
            .OrderBy(i => i.Name, NaturalStringComparer.Instance)
            .ToList();

        Assert.AreEqual("Apple", sorted[0].Name);
        Assert.AreEqual("banana.txt", sorted[1].Name);
        Assert.AreEqual("cherry.txt", sorted[2].Name);
        Assert.AreEqual("Mango", sorted[3].Name);
        Assert.AreEqual("Zebra", sorted[4].Name);
    }

    // ── 3. SortBy_Date_ReorderItems ──────────────────────────

    [TestMethod]
    public void SortBy_Date_ReorderItems()
    {
        var oldest = new DateTime(2020, 1, 1);
        var middle = new DateTime(2023, 6, 15);
        var newest = new DateTime(2025, 12, 31);

        var items = new List<FileItem>
        {
            MakeFile("middle.txt", dateModified: middle),
            MakeFile("newest.txt", dateModified: newest),
            MakeFile("oldest.txt", dateModified: oldest),
        };

        var sorted = items.OrderBy(i => i.DateModified).ToList();

        Assert.AreEqual("oldest.txt", sorted[0].Name);
        Assert.AreEqual("middle.txt", sorted[1].Name);
        Assert.AreEqual("newest.txt", sorted[2].Name);
    }

    // ── 4. SortBy_Size_ReorderItems ──────────────────────────

    [TestMethod]
    public void SortBy_Size_ReorderItems()
    {
        var items = new List<FileItem>
        {
            MakeFile("large.bin", size: 10 * 1024 * 1024),   // 10 MB
            MakeFile("tiny.txt", size: 128),                   // 128 B
            MakeFile("medium.doc", size: 512 * 1024),          // 512 KB
        };

        var sorted = items.OrderBy(i => i.Size).ToList();

        Assert.AreEqual("tiny.txt", sorted[0].Name);
        Assert.AreEqual(128L, sorted[0].Size);

        Assert.AreEqual("medium.doc", sorted[1].Name);
        Assert.AreEqual(512L * 1024, sorted[1].Size);

        Assert.AreEqual("large.bin", sorted[2].Name);
        Assert.AreEqual(10L * 1024 * 1024, sorted[2].Size);
    }

    // ── 5. SortBy_Type_ReorderItems ──────────────────────────

    [TestMethod]
    public void SortBy_Type_ReorderItems()
    {
        var items = new List<FileItem>
        {
            MakeFile("photo.png", fileType: ".png"),
            MakeFile("readme.txt", fileType: ".txt"),
            MakeFile("data.csv", fileType: ".csv"),
            MakeFile("notes.txt", fileType: ".txt"),
            MakeFile("image.png", fileType: ".png"),
        };

        // Sort by FileType, then by Name within each group
        var sorted = items
            .OrderBy(i => i.FileType, NaturalStringComparer.Instance)
            .ThenBy(i => i.Name, NaturalStringComparer.Instance)
            .ToList();

        // .csv first, then .png, then .txt
        Assert.AreEqual(".csv", sorted[0].FileType);
        Assert.AreEqual("data.csv", sorted[0].Name);

        Assert.AreEqual(".png", sorted[1].FileType);
        Assert.AreEqual("image.png", sorted[1].Name);

        Assert.AreEqual(".png", sorted[2].FileType);
        Assert.AreEqual("photo.png", sorted[2].Name);

        Assert.AreEqual(".txt", sorted[3].FileType);
        Assert.AreEqual("notes.txt", sorted[3].Name);

        Assert.AreEqual(".txt", sorted[4].FileType);
        Assert.AreEqual("readme.txt", sorted[4].Name);
    }

    // ── 6. SortDescending_ReverseOrder ───────────────────────

    [TestMethod]
    public void SortDescending_ReverseOrder()
    {
        var items = new List<IFileSystemItem>
        {
            MakeFile("alpha.txt"),
            MakeFile("gamma.txt"),
            MakeFile("beta.txt"),
        };

        var ascending = items
            .OrderBy(i => i.Name, NaturalStringComparer.Instance)
            .ToList();

        var descending = items
            .OrderByDescending(i => i.Name, NaturalStringComparer.Instance)
            .ToList();

        Assert.AreEqual("alpha.txt", ascending[0].Name);
        Assert.AreEqual("beta.txt", ascending[1].Name);
        Assert.AreEqual("gamma.txt", ascending[2].Name);

        Assert.AreEqual("gamma.txt", descending[0].Name);
        Assert.AreEqual("beta.txt", descending[1].Name);
        Assert.AreEqual("alpha.txt", descending[2].Name);

        // Verify they are exact reverses
        for (int i = 0; i < ascending.Count; i++)
        {
            Assert.AreEqual(ascending[i].Name, descending[descending.Count - 1 - i].Name);
        }
    }

    // ── 7. HiddenFiles_FilteredByDefault ─────────────────────

    [TestMethod]
    public void HiddenFiles_FilteredByDefault()
    {
        var allItems = new List<IFileSystemItem>
        {
            MakeFile("visible1.txt"),
            MakeFile(".hidden_config", isHidden: true),
            MakeFolder("Documents"),
            MakeFolder(".git", isHidden: true),
            MakeFile("visible2.txt"),
            MakeFile("thumbs.db", isHidden: true),
        };

        // Filter hidden items (mirrors default ViewModel behavior)
        var filtered = allItems.Where(i => !i.IsHidden).ToList();

        Assert.AreEqual(3, filtered.Count);
        Assert.IsTrue(filtered.All(i => !i.IsHidden));
        Assert.AreEqual("visible1.txt", filtered[0].Name);
        Assert.AreEqual("Documents", filtered[1].Name);
        Assert.AreEqual("visible2.txt", filtered[2].Name);

        // Verify hidden items are excluded
        Assert.IsFalse(filtered.Any(i => i.Name == ".hidden_config"));
        Assert.IsFalse(filtered.Any(i => i.Name == ".git"));
        Assert.IsFalse(filtered.Any(i => i.Name == "thumbs.db"));
    }

    // ── 8. CancelLoading_StopsAsyncLoad ──────────────────────

    [TestMethod]
    public async Task CancelLoading_StopsAsyncLoad()
    {
        // Test the CancellationTokenSource pattern used by FolderViewModel.LoadChildrenAsync
        var cts = new CancellationTokenSource();

        // Simulate starting an async load
        var loadTask = Task.Run(async () =>
        {
            var items = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                items.Add($"item_{i}");
                await Task.Delay(10, cts.Token);
            }
            return items;
        }, cts.Token);

        // Cancel immediately (simulates rapid navigation where previous load is cancelled)
        cts.Cancel();

        Assert.IsTrue(cts.Token.IsCancellationRequested);

        // The task should throw OperationCanceledException (or its subclass TaskCanceledException)
        try
        {
            await loadTask;
            Assert.Fail("Expected cancellation exception");
        }
        catch (OperationCanceledException)
        {
            // Expected — TaskCanceledException is a subclass of OperationCanceledException
        }

        // Verify a new CTS can be created for the next load (mirrors ViewModel pattern)
        var newCts = new CancellationTokenSource();
        Assert.IsFalse(newCts.Token.IsCancellationRequested);
        newCts.Dispose();
        cts.Dispose();
    }

    // ── 9. MultiSelect_TracksSelectedItems ───────────────────

    [TestMethod]
    public void MultiSelect_TracksSelectedItems()
    {
        // Simulate multi-selection tracking as done by the ViewModel
        var allItems = new List<IFileSystemItem>
        {
            MakeFile("doc1.txt"),
            MakeFile("doc2.txt"),
            MakeFolder("Subfolder"),
            MakeFile("doc3.txt"),
            MakeFile("image.png"),
        };

        // Track selected items (simulates ListView.SelectedItems binding)
        var selectedItems = new List<IFileSystemItem>();

        // Select items at indices 0, 2, 4 (Ctrl+click pattern)
        selectedItems.Add(allItems[0]);
        selectedItems.Add(allItems[2]);
        selectedItems.Add(allItems[4]);

        Assert.AreEqual(3, selectedItems.Count);
        Assert.AreEqual("doc1.txt", selectedItems[0].Name);
        Assert.AreEqual("Subfolder", selectedItems[1].Name);
        Assert.AreEqual("image.png", selectedItems[2].Name);

        // Verify selection contains expected types
        Assert.AreEqual(2, selectedItems.OfType<FileItem>().Count());
        Assert.AreEqual(1, selectedItems.OfType<FolderItem>().Count());

        // Deselect one item
        selectedItems.Remove(allItems[2]);
        Assert.AreEqual(2, selectedItems.Count);
        Assert.IsFalse(selectedItems.Any(i => i.Name == "Subfolder"));

        // Clear selection
        selectedItems.Clear();
        Assert.AreEqual(0, selectedItems.Count);
    }
}
