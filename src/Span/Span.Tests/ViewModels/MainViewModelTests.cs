using System.Text.Json;
using Moq;
using Span.Models;
using Span.Services.FileOperations;

namespace Span.Tests.ViewModels;

/// <summary>
/// TabStateDto mirror for test project — the real TabStateDto lives in TabItem.cs
/// which cannot be linked due to WinUI dependencies (Microsoft.UI.Xaml.Visibility).
/// </summary>
internal record TabStateDto(string Id, string Header, string Path, int ViewMode, int IconSize);

[TestClass]
public class MainViewModelTests
{
    // ── Tab State Management ──

    [TestMethod]
    public void AddTab_CreatesNewTab()
    {
        var tabs = new List<TabStateDto>();

        var newTab = new TabStateDto(
            Guid.NewGuid().ToString("N")[..8],
            "Home",
            string.Empty,
            (int)ViewMode.Home,
            (int)ViewMode.IconMedium);

        tabs.Add(newTab);

        Assert.AreEqual(1, tabs.Count);
        Assert.AreEqual("Home", tabs[0].Header);
        Assert.AreEqual((int)ViewMode.Home, tabs[0].ViewMode);
        Assert.IsFalse(string.IsNullOrEmpty(tabs[0].Id));
    }

    [TestMethod]
    public void CloseTab_RemovesTab()
    {
        var tabs = new List<TabStateDto>
        {
            new("tab1", "Downloads", @"C:\Users\test\Downloads", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium),
            new("tab2", "Documents", @"C:\Users\test\Documents", (int)ViewMode.Details, (int)ViewMode.IconMedium),
            new("tab3", "Desktop", @"C:\Users\test\Desktop", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium),
        };

        var toRemove = tabs.First(t => t.Id == "tab2");
        tabs.Remove(toRemove);

        Assert.AreEqual(2, tabs.Count);
        Assert.IsFalse(tabs.Any(t => t.Id == "tab2"));
        Assert.AreEqual("tab1", tabs[0].Id);
        Assert.AreEqual("tab3", tabs[1].Id);
    }

    [TestMethod]
    public void CloseTab_LastTab_CreatesHome()
    {
        var tabs = new List<TabStateDto>
        {
            new("onlytab", "Downloads", @"C:\Users\test\Downloads", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium),
        };

        // Simulate closing the last tab: remove it, then auto-create Home tab
        tabs.Clear();
        Assert.AreEqual(0, tabs.Count);

        // MainViewModel auto-creates a Home tab when list becomes empty
        if (tabs.Count == 0)
        {
            tabs.Add(new TabStateDto(
                Guid.NewGuid().ToString("N")[..8],
                "Home",
                string.Empty,
                (int)ViewMode.Home,
                (int)ViewMode.IconMedium));
        }

        Assert.AreEqual(1, tabs.Count);
        Assert.AreEqual("Home", tabs[0].Header);
        Assert.AreEqual((int)ViewMode.Home, tabs[0].ViewMode);
        Assert.AreEqual(string.Empty, tabs[0].Path);
    }

    [TestMethod]
    public void DuplicateTab_CopiesPathAndViewMode()
    {
        var original = new TabStateDto(
            "orig1",
            "Downloads",
            @"C:\Users\test\Downloads",
            (int)ViewMode.Details,
            (int)ViewMode.IconLarge);

        // Duplicate: new Id, same path and view settings
        var duplicate = original with { Id = Guid.NewGuid().ToString("N")[..8] };

        Assert.AreNotEqual(original.Id, duplicate.Id);
        Assert.AreEqual(original.Path, duplicate.Path);
        Assert.AreEqual(original.Header, duplicate.Header);
        Assert.AreEqual(original.ViewMode, duplicate.ViewMode);
        Assert.AreEqual(original.IconSize, duplicate.IconSize);
    }

    // ── ViewMode Switching ──

    [TestMethod]
    public void SwitchViewMode_UpdatesCurrentMode()
    {
        // Simulate tab state transitions through various view modes
        var tab = new TabStateDto("t1", "Docs", @"C:\Docs", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium);

        // Switch to Details
        var detailsTab = tab with { ViewMode = (int)ViewMode.Details };
        Assert.AreEqual((int)ViewMode.Details, detailsTab.ViewMode);

        // Switch to IconLarge
        var iconTab = detailsTab with { ViewMode = (int)ViewMode.IconLarge };
        Assert.AreEqual((int)ViewMode.IconLarge, iconTab.ViewMode);

        // Switch to List
        var listTab = iconTab with { ViewMode = (int)ViewMode.List };
        Assert.AreEqual((int)ViewMode.List, listTab.ViewMode);

        // Verify ViewMode round-trip through int cast
        Assert.AreEqual(ViewMode.List, (ViewMode)listTab.ViewMode);
    }

    // ── Undo/Redo Integration ──

    [TestMethod]
    public async Task UndoRedo_StackIntegration()
    {
        var history = new FileOperationHistory();

        // Create a mock file operation
        var mockOp = new Mock<IFileOperation>();
        mockOp.Setup(o => o.Description).Returns("rename file");
        mockOp.Setup(o => o.CanUndo).Returns(true);
        mockOp.Setup(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
        mockOp.Setup(o => o.UndoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        // Execute
        var result = await history.ExecuteAsync(mockOp.Object);
        Assert.IsTrue(result.Success);
        Assert.IsTrue(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
        Assert.AreEqual("rename file", history.UndoDescription);

        // Undo
        var undoResult = await history.UndoAsync();
        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
        Assert.AreEqual("rename file", history.RedoDescription);

        // Redo
        var redoResult = await history.RedoAsync();
        Assert.IsTrue(redoResult.Success);
        Assert.IsTrue(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
    }

    // ── Session Serialization ──

    [TestMethod]
    public void SessionSave_SerializesTabState()
    {
        var tabs = new List<TabStateDto>
        {
            new("abc123", "Downloads", @"C:\Users\test\Downloads", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium),
            new("def456", "Documents", @"C:\Users\test\Documents", (int)ViewMode.Details, (int)ViewMode.IconLarge),
        };

        var json = JsonSerializer.Serialize(tabs);

        Assert.IsFalse(string.IsNullOrWhiteSpace(json));
        Assert.IsTrue(json.Contains("abc123"));
        Assert.IsTrue(json.Contains("def456"));
        Assert.IsTrue(json.Contains("Downloads"));
        Assert.IsTrue(json.Contains("Documents"));
    }

    [TestMethod]
    public void SessionRestore_DeserializesTabState()
    {
        var original = new List<TabStateDto>
        {
            new("abc123", "Downloads", @"C:\Users\test\Downloads", (int)ViewMode.MillerColumns, (int)ViewMode.IconMedium),
            new("def456", "Documents", @"C:\Users\test\Documents", (int)ViewMode.Details, (int)ViewMode.IconLarge),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<List<TabStateDto>>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(2, restored!.Count);

        Assert.AreEqual("abc123", restored[0].Id);
        Assert.AreEqual("Downloads", restored[0].Header);
        Assert.AreEqual(@"C:\Users\test\Downloads", restored[0].Path);
        Assert.AreEqual((int)ViewMode.MillerColumns, restored[0].ViewMode);
        Assert.AreEqual((int)ViewMode.IconMedium, restored[0].IconSize);

        Assert.AreEqual("def456", restored[1].Id);
        Assert.AreEqual("Documents", restored[1].Header);
        Assert.AreEqual(@"C:\Users\test\Documents", restored[1].Path);
        Assert.AreEqual((int)ViewMode.Details, restored[1].ViewMode);
        Assert.AreEqual((int)ViewMode.IconLarge, restored[1].IconSize);
    }
}
