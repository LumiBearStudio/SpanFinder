using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for view mode switching (Miller, Details, List, Icon).
///
/// Note: WinUI 3 Grid hosts (Host_Miller, Host_Details) aren't in the UIA tree.
/// We verify mode switches by checking for mode-specific child elements:
/// - Details mode: Button_FilterName (column header buttons)
/// - Miller mode: no Details-specific elements visible
/// </summary>
[TestClass]
public class ViewModeTests
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
    public void ViewModeButton_Exists_And_Clickable()
    {
        var viewModeBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_ViewMode");
        Assert.IsTrue(viewModeBtn.IsEnabled, "View mode button should be enabled");
    }

    [TestMethod]
    public void Shortcut_Ctrl2_SwitchesToDetails()
    {
        // Ctrl+2 = Details mode
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);

        // DetailsModeView has Button_FilterName — poll until it appears
        var filterNameBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterNameBtn, "Details filter name button should exist after Ctrl+2");

        // Switch back to Miller for other tests
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Shortcut_Ctrl1_SwitchesToMiller()
    {
        // First ensure we're NOT in Miller by switching to Details
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        var filterBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Should be in Details mode before testing Ctrl+1");

        // Ctrl+1 = Miller Columns
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // In Miller mode, Details-specific elements should NOT be visible
        var filterNameBtn = SpanAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterNameBtn, "Details filter button should NOT exist in Miller mode");
    }
}
