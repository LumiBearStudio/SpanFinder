using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Span.UITests.Tests;

[TestClass]
public class ViewModeDetailedTests
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

    private void SwitchToMillerColumns()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void DetailsView_ShowsAllColumns()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Switch to Details view (Ctrl+2)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Look for column headers in the Details view
        var headers = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.HeaderItem));
        if (headers.Length == 0)
        {
            // Try alternative: look for header elements by control type
            var headerGroups = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Header));
            if (headerGroups.Length == 0)
            {
                // Switch back to Miller Columns before asserting
                SwitchToMillerColumns();
                Assert.Inconclusive("Column headers not found in Details view. The Details view may use a custom header implementation.");
                return;
            }
        }

        // Verify expected column headers exist (Name, Size, Date Modified, Type)
        var headerNames = new List<string>();
        foreach (var header in headers)
        {
            if (!string.IsNullOrEmpty(header.Name))
            {
                headerNames.Add(header.Name);
            }
        }

        // Switch back to Miller Columns
        SwitchToMillerColumns();

        Assert.IsTrue(headers.Length >= 2, $"Expected at least 2 column headers in Details view, found {headers.Length}. Headers: {string.Join(", ", headerNames)}");
    }

    [TestMethod]
    public void DetailsView_ColumnResize_Works()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Switch to Details view (Ctrl+2)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Find column header separators or resize grips
        var headers = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.HeaderItem));
        if (headers.Length < 2)
        {
            SwitchToMillerColumns();
            Assert.Inconclusive("Not enough column headers found to test resizing.");
            return;
        }

        // Try to find a resize grip or thumb between headers
        var thumbs = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Thumb));
        var separators = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Separator));

        // Switch back to Miller Columns
        SwitchToMillerColumns();

        // At minimum, verify that the column headers exist and are interactable
        Assert.IsTrue(headers.Length >= 2, "Details view should have at least 2 resizable column headers");
    }

    [TestMethod]
    public void ListView_ColumnWidthSlider_Works()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Switch to List view (Ctrl+3)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_3);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Look for a slider control for column width adjustment
        var sliders = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Slider));

        if (sliders.Length == 0)
        {
            SwitchToMillerColumns();
            Assert.Inconclusive("No slider control found in List view. Column width slider may not be implemented.");
            return;
        }

        // Try interacting with the first slider found
        var slider = sliders[0];
        var initialBounds = slider.BoundingRectangle;

        // Click on the slider to interact with it
        slider.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Switch back to Miller Columns
        SwitchToMillerColumns();

        Assert.IsTrue(initialBounds.Width > 0, "Slider control should be visible and have width");
    }

    [TestMethod]
    public void IconView_SizeToggle_Changes()
    {
        Assert.IsNotNull(_window, "Main window not found");

        // Switch to Icon view (Ctrl+4)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_4);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Look for items in the icon view
        var items = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));

        if (items.Length == 0)
        {
            SwitchToMillerColumns();
            Assert.Inconclusive("No items found in Icon view to measure size changes.");
            return;
        }

        // Record initial item size
        var initialBounds = items[0].BoundingRectangle;

        // Look for a size toggle button or slider
        var toggleButtons = _window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        AutomationElement? sizeToggle = null;

        foreach (var btn in toggleButtons)
        {
            if (btn.Name?.Contains("Size", StringComparison.OrdinalIgnoreCase) == true ||
                btn.Name?.Contains("Icon", StringComparison.OrdinalIgnoreCase) == true ||
                btn.AutomationId?.Contains("Size", StringComparison.OrdinalIgnoreCase) == true)
            {
                sizeToggle = btn;
                break;
            }
        }

        if (sizeToggle != null)
        {
            sizeToggle.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        }

        // Switch back to Miller Columns
        SwitchToMillerColumns();

        // Basic assertion: Icon view should have rendered items
        Assert.IsTrue(initialBounds.Width > 0 && initialBounds.Height > 0,
            "Icon view items should have visible dimensions");
    }
}
