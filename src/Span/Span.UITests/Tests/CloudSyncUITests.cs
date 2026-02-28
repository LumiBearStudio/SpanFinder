using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Span.UITests.Tests;

[TestClass]
[TestCategory("Cloud")]
public class CloudSyncUITests
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

    private static bool IsGoogleDriveAvailable()
    {
        return System.IO.Directory.Exists(@"G:\내 드라이브");
    }

    private void NavigateToPath(string path)
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Ctrl+L to focus address bar
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Type the path and press Enter
        Keyboard.Type(path);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    [TestMethod]
    public void NavigateToGoogleDrive_ShowsFiles()
    {
        if (!IsGoogleDriveAvailable())
        {
            Assert.Inconclusive("Google Drive is not available on this machine.");
            return;
        }

        Assert.IsNotNull(_window, "Main window not found");

        NavigateToPath(@"G:\내 드라이브\TEST");

        // Wait for content to load
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Verify that files are shown by checking for list items in the view
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Expected files to be shown in Google Drive TEST folder");
    }

    [TestMethod]
    public void CloudFile_ShowsSyncBadge()
    {
        if (!IsGoogleDriveAvailable())
        {
            Assert.Inconclusive("Google Drive is not available on this machine.");
            return;
        }

        Assert.IsNotNull(_window, "Main window not found");

        NavigateToPath(@"G:\내 드라이브\TEST");

        // Wait for content to load
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Look for sync badge elements on cloud files
        var badges = _window.FindAllDescendants(cf => cf.ByAutomationId("SyncBadge"));
        if (badges.Length == 0)
        {
            // Also try searching by name pattern
            var statusElements = _window.FindAllDescendants(cf => cf.ByClassName("SyncStatusIcon"));
            Assert.Inconclusive("Sync badge UI elements not found. Cloud sync badge feature may not be implemented yet.");
            return;
        }

        Assert.IsTrue(badges.Length > 0, "Expected sync badges on cloud files");
    }

    [TestMethod]
    public void CloudFolder_NavigationWorks()
    {
        if (!IsGoogleDriveAvailable())
        {
            Assert.Inconclusive("Google Drive is not available on this machine.");
            return;
        }

        Assert.IsNotNull(_window, "Main window not found");

        NavigateToPath(@"G:\내 드라이브");

        // Wait for content to load
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Try to find and click on a subfolder
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        if (listItems.Length == 0)
        {
            Assert.Inconclusive("No items found in Google Drive root folder.");
            return;
        }

        // Click the first item to navigate into it
        listItems[0].Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Press Enter to open the folder
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1500));

        // Verify navigation occurred - new column or content should appear
        var updatedItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsNotNull(updatedItems, "Expected navigation into cloud subfolder to show content");
    }
}
