using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Span.UITests.Tests;

/// <summary>
/// Smoke tests: verify the app launches and core UI elements are present.
/// Run these first to confirm the test infrastructure works.
/// </summary>
[TestClass]
public class SmokeTests
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
    public void App_Launches_And_MainWindow_Exists()
    {
        Assert.IsNotNull(_window, "Main window should exist after launch");
        Assert.IsTrue(_window.Title.Contains("Span") || !string.IsNullOrEmpty(_window.Title),
            "Window title should not be empty");
    }

    [TestMethod]
    public void Toolbar_Buttons_Exist()
    {
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        var forwardBtn = SpanAppFixture.FindById(_window!, "Button_Forward");
        var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");

        Assert.IsNotNull(backBtn, "Back button should exist");
        Assert.IsNotNull(forwardBtn, "Forward button should exist");
        Assert.IsNotNull(newTabBtn, "New Tab button should exist");
    }

    [TestMethod]
    public void AddressBar_Exists()
    {
        var addressBar = SpanAppFixture.FindById(_window!, "AddressBar");
        Assert.IsNotNull(addressBar, "Address bar should exist");
    }

    [TestMethod]
    public void SearchBox_Exists()
    {
        var searchBox = SpanAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist");
    }

    [TestMethod]
    public void Sidebar_Buttons_Exist()
    {
        var sidebar = SpanAppFixture.FindById(_window!, "Sidebar");
        var helpBtn = SpanAppFixture.FindById(_window!, "Button_Help");
        var settingsBtn = SpanAppFixture.FindById(_window!, "Button_Settings");

        Assert.IsNotNull(sidebar, "Sidebar should exist");
        Assert.IsNotNull(helpBtn, "Help button should exist");
        Assert.IsNotNull(settingsBtn, "Settings button should exist");
    }

    [TestMethod]
    public void FileOperation_Buttons_Exist()
    {
        var cutBtn = SpanAppFixture.FindById(_window!, "Button_Cut");
        var copyBtn = SpanAppFixture.FindById(_window!, "Button_Copy");
        var pasteBtn = SpanAppFixture.FindById(_window!, "Button_Paste");
        var renameBtn = SpanAppFixture.FindById(_window!, "Button_Rename");
        var deleteBtn = SpanAppFixture.FindById(_window!, "Button_Delete");

        Assert.IsNotNull(cutBtn, "Cut button should exist");
        Assert.IsNotNull(copyBtn, "Copy button should exist");
        Assert.IsNotNull(pasteBtn, "Paste button should exist");
        Assert.IsNotNull(renameBtn, "Rename button should exist");
        Assert.IsNotNull(deleteBtn, "Delete button should exist");
    }

    [TestMethod]
    public void ViewMode_And_Sort_Buttons_Exist()
    {
        var sortBtn = SpanAppFixture.FindById(_window!, "Button_Sort");
        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        var splitViewBtn = SpanAppFixture.FindById(_window!, "Button_SplitView");

        Assert.IsNotNull(sortBtn, "Sort button should exist");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist");
        Assert.IsNotNull(splitViewBtn, "Split view button should exist");
    }

    [TestMethod]
    public void LeftPane_Exists()
    {
        var leftPane = SpanAppFixture.FindById(_window!, "LeftPane");
        Assert.IsNotNull(leftPane, "Left pane should exist");
    }
}
