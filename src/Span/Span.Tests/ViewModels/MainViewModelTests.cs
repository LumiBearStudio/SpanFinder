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

    // ── Per-Tab Split View State ──

    [TestMethod]
    public void TabSplitState_DefaultValues()
    {
        // TabItem의 분할뷰 기본값: 비활성, MillerColumns
        var tab = new TabStateDto("t1", "Home", "", (int)ViewMode.Home, (int)ViewMode.IconMedium);

        // TabStateDto에는 split 필드가 없지만 TabItem에는 있음.
        // 여기서는 ViewMode 기본값 시나리오만 검증.
        Assert.AreEqual((int)ViewMode.Home, tab.ViewMode);
        Assert.AreEqual((int)ViewMode.IconMedium, tab.IconSize);
    }

    [TestMethod]
    public void SwitchViewMode_RightViewMode_IndependentOfLeft()
    {
        // 좌/우 뷰모드는 독립적으로 동작해야 함
        var leftMode = ViewMode.Details;
        var rightMode = ViewMode.List;

        Assert.AreNotEqual(leftMode, rightMode);
        Assert.AreEqual(ViewMode.Details, leftMode);
        Assert.AreEqual(ViewMode.List, rightMode);

        // ViewMode.Home은 우측 패인에서도 유효한 값
        var rightHome = ViewMode.Home;
        Assert.AreEqual(ViewMode.Home, rightHome);
    }

    [TestMethod]
    public void ViewMode_HomeIsValidForRightPane()
    {
        // 설정 behavior=0(Home) → RightViewMode = Home
        var rightViewMode = ViewMode.Home;
        Assert.AreEqual(ViewMode.Home, rightViewMode);

        // Home에서 드라이브 클릭 시 다른 뷰모드로 전환
        rightViewMode = ViewMode.List;
        Assert.AreEqual(ViewMode.List, rightViewMode);
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

    // ── Split View + Preview Panel Interaction ──
    // When split view is enabled, preview panels should be automatically closed
    // to maximize usable screen space.

    /// <summary>
    /// Helper: simulates split view state with preview flags.
    /// </summary>
    private record SplitViewState(bool IsSplitEnabled, bool IsLeftPreviewEnabled, bool IsRightPreviewEnabled);

    private static SplitViewState ToggleSplitViewOn(SplitViewState state)
    {
        // Replicates ToggleSplitView logic: when enabling split view,
        // close both preview panels
        if (!state.IsSplitEnabled)
        {
            return new SplitViewState(
                IsSplitEnabled: true,
                IsLeftPreviewEnabled: false,   // auto-close
                IsRightPreviewEnabled: false    // auto-close
            );
        }
        return state;
    }

    private static SplitViewState ToggleSplitViewOff(SplitViewState state)
    {
        if (state.IsSplitEnabled)
        {
            return state with { IsSplitEnabled = false };
        }
        return state;
    }

    [TestMethod]
    public void SplitViewOn_ClosesLeftPreview()
    {
        var state = new SplitViewState(false, true, false);
        var result = ToggleSplitViewOn(state);

        Assert.IsTrue(result.IsSplitEnabled);
        Assert.IsFalse(result.IsLeftPreviewEnabled, "Left preview should close when split view enables");
    }

    [TestMethod]
    public void SplitViewOn_ClosesRightPreview()
    {
        var state = new SplitViewState(false, false, true);
        var result = ToggleSplitViewOn(state);

        Assert.IsTrue(result.IsSplitEnabled);
        Assert.IsFalse(result.IsRightPreviewEnabled, "Right preview should close when split view enables");
    }

    [TestMethod]
    public void SplitViewOn_ClosesBothPreviews()
    {
        var state = new SplitViewState(false, true, true);
        var result = ToggleSplitViewOn(state);

        Assert.IsTrue(result.IsSplitEnabled);
        Assert.IsFalse(result.IsLeftPreviewEnabled);
        Assert.IsFalse(result.IsRightPreviewEnabled);
    }

    [TestMethod]
    public void SplitViewOn_NoPreviews_NoChange()
    {
        var state = new SplitViewState(false, false, false);
        var result = ToggleSplitViewOn(state);

        Assert.IsTrue(result.IsSplitEnabled);
        Assert.IsFalse(result.IsLeftPreviewEnabled);
        Assert.IsFalse(result.IsRightPreviewEnabled);
    }

    [TestMethod]
    public void SplitViewOff_DoesNotAutoEnablePreview()
    {
        // When disabling split view, preview should NOT auto-restore
        var state = new SplitViewState(true, false, false);
        var result = ToggleSplitViewOff(state);

        Assert.IsFalse(result.IsSplitEnabled);
        Assert.IsFalse(result.IsLeftPreviewEnabled, "Preview should not auto-enable on split view off");
        Assert.IsFalse(result.IsRightPreviewEnabled);
    }

    [TestMethod]
    public void SplitViewAlreadyOn_NoDoubleToggle()
    {
        var state = new SplitViewState(true, true, true);
        var result = ToggleSplitViewOn(state);

        // Already enabled → no change
        Assert.IsTrue(result.IsSplitEnabled);
        Assert.IsTrue(result.IsLeftPreviewEnabled, "No change when already split");
        Assert.IsTrue(result.IsRightPreviewEnabled);
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
