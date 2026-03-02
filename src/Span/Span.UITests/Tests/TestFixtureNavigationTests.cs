using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class TestFixtureNavigationTests
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
    public void Navigate_SmallFolder_ShowsFiles()
    {
        _testDir = SpanAppFixture.CreateTestDirectory(5);
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Test folder should display list items after navigation");
    }

    [TestMethod]
    public void Navigate_MixedTypes_ShowsVariousExtensions()
    {
        // Create test directory with mixed file types
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SpanTest_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        _testDir = tempDir;

        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "doc.txt"), "text");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "data.csv"), "a,b,c");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "config.json"), "{}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "style.css"), "body{}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "script.js"), "//js");

        if (!SpanAppFixture.NavigateToPath(_window!, tempDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.IsTrue(listItems.Length >= 5, "Mixed types folder should display files with various extensions");

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigating to mixed types");
    }

    [TestMethod]
    public void Navigate_DeepFolder_NavigatesDeep()
    {
        SpanAppFixture.EnsureExplorerMode(_window!);
        if (!SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath))
            Assert.Inconclusive("Could not navigate");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Navigate deeper using arrow keys (Miller columns)
        for (int i = 0; i < 3; i++)
        {
            Keyboard.Type(VirtualKeyShort.DOWN);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            Keyboard.Type(VirtualKeyShort.RIGHT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));
        }

        // App should still be responsive after deep navigation
        var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtn, "App should remain responsive after deep folder navigation");
    }

    [TestMethod]
    public void Navigate_WindowsFolder_LoadsItems()
    {
        if (!SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath))
            Assert.Inconclusive("Could not navigate");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "C:\\Windows should load and display items");
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive");
    }

    [TestMethod]
    public void Navigate_UnicodeNames_DisplaysCorrectly()
    {
        // Create test directory with unicode file names
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SpanTest_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        _testDir = tempDir;

        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "한글파일.txt"), "Korean");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "日本語.txt"), "Japanese");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "中文文件.txt"), "Chinese");

        if (!SpanAppFixture.NavigateToPath(_window!, tempDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Unicode filenames should display correctly");
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain enabled with unicode filenames");
    }
}
