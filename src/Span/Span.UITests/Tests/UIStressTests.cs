using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Span.UITests.Tests;

[TestClass]
[TestCategory("Stress")]
public class UIStressTests
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
    public void Stress_Open50Tabs_NoFreeze()
    {
        Assert.IsNotNull(_window, "Main window not found");

        const int tabCount = 50;

        // Open 50 tabs rapidly via Ctrl+T
        for (int i = 0; i < tabCount; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
        }

        // Wait for UI to stabilize
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Verify the window is still responsive by checking it exists
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after opening 50 tabs");

        // Close all tabs (keep at least one)
        for (int i = 0; i < tabCount; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Verify window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after closing tabs");
    }

    [TestMethod]
    public void Stress_RapidViewModeSwitch_100Times()
    {
        Assert.IsNotNull(_window, "Main window not found");

        var viewModeKeys = new[]
        {
            VirtualKeyShort.KEY_1, // Miller Columns
            VirtualKeyShort.KEY_2, // Details
            VirtualKeyShort.KEY_3, // List
            VirtualKeyShort.KEY_4  // Icons
        };

        // Cycle through view modes 100 times
        for (int i = 0; i < 100; i++)
        {
            var key = viewModeKeys[i % viewModeKeys.Length];
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, key);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
        }

        // Wait for UI to stabilize
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Switch back to Miller Columns
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after 100 view mode switches");
    }

    [TestMethod]
    public void Stress_RapidNavigation_BackForward()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Navigate to a known folder first via address bar
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(@"C:\");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Navigate into some folders by selecting items
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        if (listItems.Length > 0)
        {
            // Click first few items to build navigation history
            for (int i = 0; i < Math.Min(3, listItems.Length); i++)
            {
                listItems[i].Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
                Keyboard.Press(VirtualKeyShort.ENTER);
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Refresh list items for the new view
                listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
                if (listItems.Length == 0) break;
            }
        }

        // Rapid back/forward navigation 50 times
        for (int i = 0; i < 50; i++)
        {
            // Alt+Left for back
            Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.LEFT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
        }

        for (int i = 0; i < 50; i++)
        {
            // Alt+Right for forward
            Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
        }

        // Wait for UI to stabilize
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Verify window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after rapid back/forward navigation");
    }

    [TestMethod]
    public void Stress_TypeAhead_RapidKeys()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Make sure we're in Miller Columns mode
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Click on the first column to ensure it has focus
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        if (listItems.Length > 0)
        {
            listItems[0].Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }

        // Rapidly type characters for type-ahead search
        var searchChars = "abcdefghijklmnopqrstuvwxyz";
        for (int round = 0; round < 5; round++)
        {
            foreach (char c in searchChars)
            {
                Keyboard.Type(c.ToString());
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(20));
            }
            // Brief pause between rounds to let the buffer reset
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(900));
        }

        // Wait for UI to stabilize
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Verify window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after rapid type-ahead input");
    }

    [TestMethod]
    public void Stress_LargeFolder_1000Files()
    {
        Assert.IsNotNull(_window, "Main window not found");

        var largeFolderPath = @"E:\TEST\LargeFolder";
        if (!System.IO.Directory.Exists(largeFolderPath))
        {
            Assert.Inconclusive($"Large folder test directory not found: {largeFolderPath}");
            return;
        }

        // Navigate to the large folder via address bar
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(largeFolderPath);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Press(VirtualKeyShort.ENTER);

        // Wait longer for large folder to load
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(5000));

        // Verify items are loaded
        var listItems = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Large folder should show files after loading");

        // Verify the window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after loading large folder");
    }

    [TestMethod]
    public void Stress_SplitView_DualNavigation()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Try to toggle split view - look for split view button or use keyboard shortcut
        var splitButtons = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        AutomationElement? splitButton = null;

        foreach (var btn in splitButtons)
        {
            if (btn.Name?.Contains("Split", StringComparison.OrdinalIgnoreCase) == true ||
                btn.AutomationId?.Contains("Split", StringComparison.OrdinalIgnoreCase) == true)
            {
                splitButton = btn;
                break;
            }
        }

        if (splitButton == null)
        {
            Assert.Inconclusive("Split view button not found. Split view feature may not be implemented.");
            return;
        }

        // Toggle split view on
        splitButton.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Navigate in first pane
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(@"C:\");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Rapid navigation in split view
        for (int i = 0; i < 20; i++)
        {
            // Navigate down
            Keyboard.Press(VirtualKeyShort.DOWN);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
            Keyboard.Press(VirtualKeyShort.ENTER);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(100));
        }

        // Wait for UI to stabilize
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // Toggle split view off
        splitButton.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Verify window is still responsive
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after split view navigation");
    }
}
