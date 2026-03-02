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
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));

        var rightViewMode = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 5000);
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
}
