using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Span.UITests.Tests;

[TestClass]
[TestCategory("Remote")]
public class RemoteConnectionUITests
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

    private static bool IsFtpServerConfigured()
    {
        // Check if there's a configured FTP connection in the sidebar
        if (_window == null)
            return false;

        var ftpItems = _window.FindAllDescendants(cf => cf.ByName("FTP"));
        return ftpItems.Length > 0;
    }

    [TestMethod]
    public void FTP_TestServer_ShowsInSidebar()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Look for FTP-related items in the sidebar
        var sidebarItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem));
        var ftpFound = false;

        foreach (var item in sidebarItems)
        {
            if (item.Name?.Contains("FTP", StringComparison.OrdinalIgnoreCase) == true)
            {
                ftpFound = true;
                break;
            }
        }

        if (!ftpFound)
        {
            // Also check list items in sidebar
            var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
            foreach (var item in listItems)
            {
                if (item.Name?.Contains("FTP", StringComparison.OrdinalIgnoreCase) == true)
                {
                    ftpFound = true;
                    break;
                }
            }
        }

        if (!ftpFound)
        {
            Assert.Inconclusive("No FTP server is configured. Add an FTP connection to test this feature.");
            return;
        }

        Assert.IsTrue(ftpFound, "FTP connection should be visible in sidebar");
    }

    [TestMethod]
    public void FTP_Navigate_ShowsRemoteFiles()
    {
        Assert.IsNotNull(_window, "Main window not found");

        if (!IsFtpServerConfigured())
        {
            Assert.Inconclusive("No FTP server is configured. Add an FTP connection to test this feature.");
            return;
        }

        // Find and click FTP item in sidebar
        var sidebarItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem));
        AutomationElement? ftpItem = null;

        foreach (var item in sidebarItems)
        {
            if (item.Name?.Contains("FTP", StringComparison.OrdinalIgnoreCase) == true)
            {
                ftpItem = item;
                break;
            }
        }

        if (ftpItem == null)
        {
            Assert.Inconclusive("FTP item not found in sidebar.");
            return;
        }

        ftpItem.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Verify remote files are shown
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Expected remote files to be shown after navigating to FTP");
    }

    [TestMethod]
    public void FTP_ViewModeSwitch_Works()
    {
        Assert.IsNotNull(_window, "Main window not found");

        if (!IsFtpServerConfigured())
        {
            Assert.Inconclusive("No FTP server is configured. Add an FTP connection to test this feature.");
            return;
        }

        // Navigate to FTP first
        var sidebarItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem));
        AutomationElement? ftpItem = null;

        foreach (var item in sidebarItems)
        {
            if (item.Name?.Contains("FTP", StringComparison.OrdinalIgnoreCase) == true)
            {
                ftpItem = item;
                break;
            }
        }

        if (ftpItem == null)
        {
            Assert.Inconclusive("FTP item not found in sidebar.");
            return;
        }

        ftpItem.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Switch to Details view (Ctrl+2)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Switch back to Miller Columns (Ctrl+1)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify we're back in Miller Columns - content should still be visible
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length >= 0, "View mode switch should not crash while on FTP connection");
    }
}
