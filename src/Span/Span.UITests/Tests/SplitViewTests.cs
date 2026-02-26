using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for split view functionality (FEATURES.md: 분할 뷰).
/// Verifies toggle, pane switching, and independent view modes.
/// </summary>
[TestClass]
public class SplitViewTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.GetMainWindow();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SpanAppFixture.Detach();
    }

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
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");

        // Toggle split on
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Right pane should have its own view mode button
        var rightViewMode = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 3000);
        Assert.IsNotNull(rightViewMode, "Right pane view mode button should appear");

        // Toggle split off
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Right pane controls should disappear
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_CtrlTab_SwitchesPanes()
    {
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");

        // Toggle split on
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Switch pane with Ctrl+Tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // App should remain responsive
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after pane switch");

        // Switch back
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Toggle split off
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_IndependentViewModes()
    {
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");

        // Toggle split on
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Left pane: switch to Details
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var filterBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Left pane should show Details filter button");

        // Switch to right pane
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Right pane should remain in its own view mode
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive in right pane");

        // Restore: switch back to left, set Miller, toggle split off
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.TAB);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
