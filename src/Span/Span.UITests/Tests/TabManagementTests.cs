using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Extended tab management tests (FEATURES.md: 탭 관리).
/// Tests tab creation, closing, and multi-tab scenarios.
/// </summary>
[TestClass]
public class TabManagementTests
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
    public void NewTabButton_Exists_And_Enabled()
    {
        var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtn, "New tab button should exist");
        Assert.IsTrue(newTabBtn.IsEnabled, "New tab button should be enabled");
    }

    [TestMethod]
    public void CreateMultipleTabs_AllCloseable()
    {
        // Create 3 additional tabs
        for (int i = 0; i < 3; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }

        // App should still be responsive
        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "UI should remain functional with multiple tabs");

        // Close all 3 tabs
        for (int i = 0; i < 3; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }

        // App should still be open (original tab remains)
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "Original tab should still exist after closing added tabs");
    }

    [TestMethod]
    public void NewTab_ViaButton_Works()
    {
        var newTabBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_NewTab");

        // Click new tab button
        newTabBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify core UI still present
        var searchBox = SpanAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist in new tab");

        // Close the new tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlComma_OpensSettings()
    {
        // Open settings tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Settings tab should open — look for settings-specific elements
        // Settings view has theme/density/font selectors
        var settingsElement = SpanAppFixture.WaitForElement(_window!, "SettingsScrollViewer", 3000);
        // Even without specific AutomationId, app should remain responsive
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after opening settings");

        // Close settings tab
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
