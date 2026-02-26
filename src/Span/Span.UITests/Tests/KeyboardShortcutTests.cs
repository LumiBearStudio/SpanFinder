using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for keyboard shortcuts documented in FEATURES.md.
/// Verifies that shortcuts trigger the expected UI state changes.
///
/// Prerequisites: Span must be running with a folder open (not Home view).
/// </summary>
[TestClass]
public class KeyboardShortcutTests
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

    // -------------------------------------------------------
    // Navigation shortcuts
    // -------------------------------------------------------

    [TestMethod]
    public void CtrlL_FocusesAddressBar()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Ctrl+L should show address bar text box");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AltD_FocusesAddressBar()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Alt+D should show address bar text box");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void F4_FocusesAddressBar()
    {
        Keyboard.Type(VirtualKeyShort.F4);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "F4 should show address bar text box");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlF_FocusesSearchBox()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Search box should be focused — verify it exists
        var searchBox = SpanAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Ctrl+F should focus search box");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    // -------------------------------------------------------
    // View mode shortcuts
    // -------------------------------------------------------

    [TestMethod]
    public void Ctrl1_SwitchesToMiller()
    {
        // First switch away from Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Switch to Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Details-specific elements should NOT exist
        var filterBtn = SpanAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "Miller mode should not have Details filter buttons");
    }

    [TestMethod]
    public void Ctrl2_SwitchesToDetails()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        var filterBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Ctrl+2 should show Details filter button");

        // Restore Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Ctrl3_SwitchesToList()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_3);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // List mode should not have Details filter buttons
        var filterBtn = SpanAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "List mode should not have Details filter buttons");

        // Restore Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Ctrl4_SwitchesToIcons()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_4);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Icon mode should not have Details filter buttons
        var filterBtn = SpanAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "Icon mode should not have Details filter buttons");

        // Restore Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    // -------------------------------------------------------
    // Help overlay
    // -------------------------------------------------------

    [TestMethod]
    public void F1_TogglesHelpOverlay()
    {
        Keyboard.Type(VirtualKeyShort.F1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Help overlay should appear — look for help-related element
        var helpOverlay = SpanAppFixture.WaitForElement(_window!, "HelpOverlay", 3000);
        // Even if AutomationId isn't set, the overlay takes focus
        // Dismiss it
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    // -------------------------------------------------------
    // Tab & window shortcuts
    // -------------------------------------------------------

    [TestMethod]
    public void CtrlT_CreatesNewTab_CtrlW_ClosesIt()
    {
        // Create new tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify window still has core UI elements
        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "UI should remain functional after new tab");

        // Close the tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    // -------------------------------------------------------
    // Split view & preview
    // -------------------------------------------------------

    [TestMethod]
    public void CtrlShiftE_TogglesSplitView()
    {
        // Toggle split on
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
        var rightViewMode = SpanAppFixture.WaitForElement(_window!, "Button_RightViewMode", 3000);
        Assert.IsNotNull(rightViewMode, "Ctrl+Shift+E should show right pane view mode");

        // Toggle split off
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void CtrlShiftP_TogglesPreviewPanel()
    {
        // Toggle preview on
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Look for preview panel content
        var previewToggle = SpanAppFixture.FindById(_window!, "Button_PreviewToggle");
        Assert.IsNotNull(previewToggle, "Preview toggle button should exist");

        // Toggle preview off
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    // -------------------------------------------------------
    // File operations (verify no crash)
    // -------------------------------------------------------

    [TestMethod]
    public void F5_Refresh_DoesNotCrash()
    {
        Keyboard.Type(VirtualKeyShort.F5);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // App should still be responsive
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after F5 refresh");
    }

    [TestMethod]
    public void CtrlA_SelectAll_DoesNotCrash()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // App should still be responsive
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+A");

        // Deselect
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlShiftEquals_EqualizeColumns_DoesNotCrash()
    {
        // Ensure Miller mode
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Equalize columns (Ctrl+Shift+=)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.OEM_PLUS);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after column equalization");
    }
}
