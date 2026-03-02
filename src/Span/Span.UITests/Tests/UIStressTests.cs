using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

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
        SpanAppFixture.Focus(_window);
        SpanAppFixture.EnsureExplorerMode(_window);
    }

    [ClassCleanup]
    public static void ClassCleanup() => SpanAppFixture.Detach();

    [TestInitialize]
    public void TestInit() => SpanAppFixture.Focus(_window!);

    [TestMethod]
    public void Stress_Open10Tabs_NoFreeze()
    {
        const int tabCount = 10;

        for (int i = 0; i < tabCount; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));
        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain responsive after opening tabs");

        for (int i = 0; i < tabCount; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after closing tabs");
    }

    [TestMethod]
    public void Stress_RapidViewModeSwitch_20Times()
    {
        var viewModeKeys = new[]
        {
            VirtualKeyShort.KEY_1,
            VirtualKeyShort.KEY_2,
            VirtualKeyShort.KEY_3,
            VirtualKeyShort.KEY_4
        };

        for (int i = 0; i < 20; i++)
        {
            var key = viewModeKeys[i % viewModeKeys.Length];
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, key);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain responsive after view mode switches");
    }

    [TestMethod]
    public void Stress_RapidNavigation_BackForward()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        // Navigate into some folders
        for (int i = 0; i < 3; i++)
        {
            Keyboard.Type(VirtualKeyShort.DOWN);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
            Keyboard.Type(VirtualKeyShort.RIGHT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }

        // Rapid back navigation 10 times
        for (int i = 0; i < 10; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.LEFT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(100));
        }

        // Rapid forward navigation 10 times
        for (int i = 0; i < 10; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(100));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));
        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain responsive after rapid back/forward navigation");
    }

    [TestMethod]
    public void Stress_TypeAhead_RapidKeys()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        // Click on the list to ensure focus
        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        if (listItems.Length > 0)
        {
            listItems[0].Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }

        // Rapidly type characters for type-ahead search
        var searchChars = "abcdefghij";
        for (int round = 0; round < 3; round++)
        {
            foreach (char c in searchChars)
            {
                Keyboard.Type(c.ToString());
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(50));
            }
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(900));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after rapid type-ahead input");
    }

    [TestMethod]
    public void Stress_LargeFolder_WindowsSystem32()
    {
        // Use C:\Windows\System32 as a real large folder
        if (!SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPathSub))
            Assert.Inconclusive("Could not navigate to System32");

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "System32 should show files after loading");
        Assert.IsTrue(_window.Properties.IsEnabled, "Window should remain responsive after loading large folder");
    }

    [TestMethod]
    public void Stress_RecursiveSearch_RapidSearchCancel_5Times()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        var queries = new[] { "*.exe", "*.dll", "*.txt", "*.sys", "*.log" };

        for (int i = 0; i < 5; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            Keyboard.Type(queries[i]);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));

            Keyboard.Press(VirtualKeyShort.ENTER);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        }

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));
        Assert.IsTrue(_window!.Properties.IsEnabled,
            "Window should remain responsive after rapid search/cancel cycles");
    }

    [TestMethod]
    public void Stress_RecursiveSearch_LargeResults_NoFreeze()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type("*.dll");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));

        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(10000));

        Assert.IsTrue(_window!.Properties.IsEnabled,
            "Window should not freeze when recursive search returns many results");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        Assert.IsTrue(_window.Properties.IsEnabled,
            "Window should remain responsive after canceling large result search");
    }
}
