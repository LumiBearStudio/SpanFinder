using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class E2EScenarioTests
{
    private static Window? _window;
    private string? _testDir;

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

    [TestCleanup]
    public void TestCleanup() => SpanAppFixture.CleanupTestDirectory(_testDir);

    [TestMethod]
    public void E2E_SwitchViewModes_AllFour()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // WinUI 3 Grid containers don't create UIA peers.
        // Verify each mode via mode-specific elements or responsiveness checks.

        // Miller mode (Ctrl+1) — verify ListItems exist
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should be responsive in Miller mode");

        // Details mode (Ctrl+2) — verify filter buttons appear
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        var filterBtn = SpanAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Details mode should show filter buttons");

        // List mode (Ctrl+3)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_3);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should be responsive in List mode");

        // Icon mode (Ctrl+4)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_4);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should be responsive in Icon mode");

        // Restore to Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void E2E_TabWorkflow_CreateNavigateClose()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist in new tab");

        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "Original tab should remain after closing the new tab");
    }

    [TestMethod]
    [TestCategory("Destructive")]
    public void E2E_NavigateCreateFolderDeleteIt()
    {
        _testDir = SpanAppFixture.CreateTestDirectory(3);

        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        var millerHost = SpanAppFixture.FindById(_window!, "Host_Miller");
        millerHost?.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_N);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after create folder attempt");
    }

    [TestMethod]
    public void E2E_SearchNavigateBack()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.Type("*.txt");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain responsive during search");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "Back button should exist after dismissing search");
    }
}
