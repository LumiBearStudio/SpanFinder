using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class SplitViewTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.GetMainWindow();
        SpanAppFixture.Focus(_window);
        SpanAppFixture.EnsureExplorerMode(_window);
    }

    [ClassCleanup]
    public static void ClassCleanup() => SpanAppFixture.Detach();

    [TestInitialize]
    public void TestInit() => SpanAppFixture.Focus(_window!);

    [TestMethod]
    public void SplitViewButton_Exists_And_Enabled()
    {
        var splitBtn = SpanAppFixture.FindById(_window!, "Button_SplitView");
        Assert.IsNotNull(splitBtn, "Split view button should exist");
        Assert.IsTrue(splitBtn.IsEnabled, "Split view button should be enabled");
    }

    [TestMethod]
    public void SplitView_Toggle_ShowsRightPane()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1200));

        var rightViewMode = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 8000);
        Assert.IsNotNull(rightViewMode, "Right pane view mode button should appear");

        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void SplitView_CtrlTab_SwitchesPanes()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtn, "App should remain responsive after pane switch");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_IndependentViewModes()
    {
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var filterBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Left pane should show Details filter button");

        // Restore
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_TabSwitch_RestoresState()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);

        // Tab 1: 분할뷰 켜기
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        var rightBtn = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 5000);
        Assert.IsNotNull(rightBtn, "Right pane should appear after split toggle");

        // Tab 2 생성 (기본 = 분할뷰 OFF)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        // Tab 2에서는 분할뷰 OFF 확인: RightViewMode 버튼 안 보여야 함
        // (새 탭은 Home이므로 split disabled)

        // Tab 2 닫기 → Tab 1로 복귀
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        // Tab 1: 분할뷰 상태 복원 확인
        var restoredBtn = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 5000);
        Assert.IsNotNull(restoredBtn, "Split view should be restored when returning to Tab 1");

        // Cleanup: 분할뷰 끄기
        splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_SettingsTab_PreservesSplitState()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);

        // 분할뷰 켜기
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        var rightBtn = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 5000);
        Assert.IsNotNull(rightBtn, "Right pane should appear");

        // Settings 탭 열기 (Ctrl+,)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        // Settings 탭 닫기 (Ctrl+W)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        // 원래 탭으로 복귀 → 분할뷰 복원 확인
        var restoredBtn = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 5000);
        Assert.IsNotNull(restoredBtn, "Split view should be preserved after Settings tab round-trip");

        // Cleanup
        splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_RightPane_AllViewModes_NoCrash()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);

        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        // 우측 패인으로 포커스 전환
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 우측 패인에서 모든 뷰모드 전환 (Miller→Details→List→Icon→Miller)
        var modes = new[] {
            VirtualKeyShort.KEY_1, VirtualKeyShort.KEY_2,
            VirtualKeyShort.KEY_3, VirtualKeyShort.KEY_4,
            VirtualKeyShort.KEY_1
        };

        foreach (var mode in modes)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, mode);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            Assert.IsTrue(_window!.Properties.IsEnabled, $"App should not crash on right pane view mode switch (Ctrl+{mode})");
        }

        // Cleanup: 좌측으로 복귀 후 분할뷰 끄기
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
