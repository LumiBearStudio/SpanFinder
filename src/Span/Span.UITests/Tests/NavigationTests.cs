using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for basic navigation: tabs, address bar, sidebar, back/forward.
/// </summary>
[TestClass]
public class NavigationTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.LaunchOrAttach();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SpanAppFixture.Close();
    }

    [TestMethod]
    public void NewTab_Click_CreatesTab()
    {
        var tabRepeater = SpanAppFixture.FindById(_window!, "TabRepeater");
        Assert.IsNotNull(tabRepeater, "Tab repeater should exist");

        // Count initial tabs
        var initialChildren = tabRepeater.FindAllChildren();
        int initialCount = initialChildren.Length;

        // Click New Tab
        var newTabBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_NewTab");
        newTabBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify tab count increased
        var afterChildren = tabRepeater.FindAllChildren();
        Assert.IsTrue(afterChildren.Length > initialCount,
            $"Tab count should increase after new tab click. Before: {initialCount}, After: {afterChildren.Length}");

        // Close the new tab with Ctrl+W
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AddressBar_Click_ShowsTextBox()
    {
        var addressBar = SpanAppFixture.FindByIdOrThrow(_window!, "AddressBar");
        addressBar.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var textBox = SpanAppFixture.FindById(_window!, "TextBox_AddressBar");
        Assert.IsNotNull(textBox, "Address bar text box should appear after click");

        // Press Escape to dismiss
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
    }

    [TestMethod]
    public void Keyboard_CtrlL_FocusesAddressBar()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var textBox = SpanAppFixture.FindById(_window!, "TextBox_AddressBar");
        Assert.IsNotNull(textBox, "Address bar should appear after Ctrl+L");

        // Press Escape to dismiss
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
    }

    [TestMethod]
    public void SplitView_Toggle_ShowsRightPane()
    {
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");

        // Toggle split view on
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var rightPane = SpanAppFixture.FindById(_window!, "RightPane");
        Assert.IsNotNull(rightPane, "Right pane should appear after split toggle");

        // Toggle split view off
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
