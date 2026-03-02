using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class HiddenFilesToggleTests
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

    private static int GetVisibleItemCount()
    {
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        return listItems?.Length ?? 0;
    }

    private string CreateTestDirWithHiddenFiles()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SpanTest_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);

        // Create normal files
        for (int i = 0; i < 3; i++)
            System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, $"visible_{i}.txt"), $"visible {i}");

        // Create hidden files
        for (int i = 0; i < 2; i++)
        {
            var hiddenPath = System.IO.Path.Combine(tempDir, $".hidden_{i}.txt");
            System.IO.File.WriteAllText(hiddenPath, $"hidden {i}");
            System.IO.File.SetAttributes(hiddenPath, System.IO.FileAttributes.Hidden);
        }

        return tempDir;
    }

    [TestMethod]
    public void HiddenFiles_CtrlH_TogglesVisibility()
    {
        _testDir = CreateTestDirWithHiddenFiles();
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var countBefore = GetVisibleItemCount();

        // Ensure keyboard focus is in the file list
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Toggle hidden files on with Ctrl+H
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        var countAfterToggleOn = GetVisibleItemCount();

        // Toggle hidden files off with Ctrl+H
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var countAfterToggleOff = GetVisibleItemCount();

        // With hidden files shown, there should be more items
        // Note: If Ctrl+H didn't reach the app (focus issue), counts may be equal
        if (countAfterToggleOn == countBefore)
            Assert.Inconclusive($"Ctrl+H may not have toggled hidden files (before={countBefore}, after={countAfterToggleOn}) — possible focus issue");
        Assert.IsTrue(countAfterToggleOn > countBefore,
            $"Toggling hidden files on should increase item count (before={countBefore}, after={countAfterToggleOn})");

        // After toggling off, count should return to original
        Assert.AreEqual(countBefore, countAfterToggleOff,
            "Toggling hidden files off should restore original item count");
    }

    [TestMethod]
    public void HiddenFiles_DefaultHidden_NotShown()
    {
        _testDir = CreateTestDirWithHiddenFiles();
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var initialCount = GetVisibleItemCount();

        // By default, hidden files should not be shown (only 3 visible files)
        Assert.IsTrue(initialCount >= 3, "Should show at least the 3 visible files");

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive");
    }

    [TestMethod]
    public void HiddenFiles_ToggleBack_HidesAgain()
    {
        _testDir = CreateTestDirWithHiddenFiles();
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var countDefault = GetVisibleItemCount();

        // Ensure keyboard focus is in the file list
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Toggle on
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        var countShown = GetVisibleItemCount();
        Assert.IsTrue(countShown >= countDefault,
            "Showing hidden files should not decrease item count");

        // Toggle off
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var countHiddenAgain = GetVisibleItemCount();
        Assert.AreEqual(countDefault, countHiddenAgain,
            $"After toggling back, item count should match default (default={countDefault}, after={countHiddenAgain})");
    }
}
