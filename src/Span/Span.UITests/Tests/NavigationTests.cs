using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for basic navigation: tabs, address bar, sidebar, back/forward.
///
/// Note: WinUI 3 Grid/Border/ItemsRepeater elements don't have AutomationPeers,
/// so container-level assertions use child elements or keyboard-driven verification.
/// </summary>
[TestClass]
public class NavigationTests
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
    public void NewTab_Click_CreatesAndCloses()
    {
        // WinUI 3 ItemsRepeater isn't in UIA tree, so we can't count tabs.
        // Instead verify New Tab + Ctrl+W cycle completes without error.
        var newTabBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_NewTab");
        newTabBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // The new tab should be active — verify the main content area is still functional
        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist after creating new tab");

        // Close the new tab with Ctrl+W
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Keyboard_CtrlL_ShowsAddressBarTextBox()
    {
        // Activate address bar edit mode via Ctrl+L
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);

        // AutoSuggestBox transitions from Collapsed → Visible; poll until it appears
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar text box should appear after Ctrl+L");

        // Press Escape to dismiss
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SplitView_Toggle_ShowsRightPaneControls()
    {
        var splitBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_SplitView");

        // Toggle split view on
        splitBtn.Click();

        // WinUI 3 Grid containers aren't in UIA tree —
        // verify right pane by finding its view mode button
        var rightViewModeBtn = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 3000);
        Assert.IsNotNull(rightViewModeBtn, "Right pane view mode button should appear after split toggle");

        // Toggle split view off
        splitBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
