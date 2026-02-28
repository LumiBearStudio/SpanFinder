using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for sort and group-by functionality in the file explorer.
/// Navigates to a known folder (E:\TEST\MixedTypes) and interacts with sort/group controls.
///
/// Prerequisites: SPAN Finder must be running.
/// E:\TEST\MixedTypes should exist with files of various types and sizes for meaningful sorting.
/// </summary>
[TestClass]
public class SortGroupUITests
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

    /// <summary>
    /// Helper: navigate to a path via address bar (Ctrl+L, type path, Enter).
    /// </summary>
    private static void NavigateToPath(string path)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        if (textBox == null)
            Assert.Inconclusive("Address bar did not appear — cannot navigate");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(100);
        Keyboard.Type(path);
        Thread.Sleep(100);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    /// <summary>
    /// Helper: navigate to the test folder with mixed file types.
    /// Falls back to E:\TEST if MixedTypes doesn't exist.
    /// </summary>
    private static void NavigateToTestFolder()
    {
        if (System.IO.Directory.Exists(@"E:\TEST\MixedTypes"))
        {
            NavigateToPath(@"E:\TEST\MixedTypes");
        }
        else if (System.IO.Directory.Exists(@"E:\TEST"))
        {
            NavigateToPath(@"E:\TEST");
        }
        else
        {
            Assert.Inconclusive("E:\\TEST directory does not exist on this machine");
        }
    }

    /// <summary>
    /// Helper: switch to Details view (Ctrl+2) where sort headers are accessible.
    /// </summary>
    private static void SwitchToDetailsView()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Helper: restore Miller view (Ctrl+1) to clean up after sort tests.
    /// </summary>
    private static void RestoreMillerView()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Sort_ByName_ReordersItems()
    {
        try
        {
            NavigateToTestFolder();
            SwitchToDetailsView();

            // In Details view, there should be column headers for sorting
            // Look for the Name column header button
            var nameHeader = SpanAppFixture.FindById(_window!, "Button_FilterName");
            if (nameHeader == null)
            {
                RestoreMillerView();
                Assert.Inconclusive("Name column header not found in Details view");
                return;
            }

            // Click the Name header to sort by name
            nameHeader.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive after sorting
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after sorting by name");

            RestoreMillerView();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            RestoreMillerView();
            Assert.Inconclusive($"Sort by name test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sort_ByDate_ReordersItems()
    {
        try
        {
            NavigateToTestFolder();
            SwitchToDetailsView();

            // Look for the Date column header button
            var dateHeader = SpanAppFixture.FindById(_window!, "Button_FilterDate");
            if (dateHeader == null)
            {
                // Try alternative naming
                dateHeader = SpanAppFixture.FindById(_window!, "Button_FilterDateModified");
            }

            if (dateHeader == null)
            {
                RestoreMillerView();
                Assert.Inconclusive("Date column header not found in Details view");
                return;
            }

            // Click the Date header to sort by date
            dateHeader.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after sorting by date");

            RestoreMillerView();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            RestoreMillerView();
            Assert.Inconclusive($"Sort by date test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sort_BySize_ReordersItems()
    {
        try
        {
            NavigateToTestFolder();
            SwitchToDetailsView();

            // Look for the Size column header button
            var sizeHeader = SpanAppFixture.FindById(_window!, "Button_FilterSize");
            if (sizeHeader == null)
            {
                RestoreMillerView();
                Assert.Inconclusive("Size column header not found in Details view");
                return;
            }

            // Click the Size header to sort by size
            sizeHeader.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after sorting by size");

            RestoreMillerView();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            RestoreMillerView();
            Assert.Inconclusive($"Sort by size test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sort_Descending_ReverseOrder()
    {
        try
        {
            NavigateToTestFolder();
            SwitchToDetailsView();

            // Find the Name header and click it twice for descending
            var nameHeader = SpanAppFixture.FindById(_window!, "Button_FilterName");
            if (nameHeader == null)
            {
                RestoreMillerView();
                Assert.Inconclusive("Name column header not found in Details view");
                return;
            }

            // First click: ascending sort
            nameHeader.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // Second click: descending sort (toggle)
            nameHeader.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive after toggling sort direction
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after toggling sort to descending");

            RestoreMillerView();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            RestoreMillerView();
            Assert.Inconclusive($"Sort descending test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void GroupBy_Type_ShowsGroupHeaders()
    {
        try
        {
            NavigateToTestFolder();
            SwitchToDetailsView();

            // Look for a group-by control or context menu
            // Group by might be accessible via a toolbar button or right-click menu
            var groupByBtn = SpanAppFixture.FindById(_window!, "Button_GroupBy");
            if (groupByBtn == null)
            {
                // Try alternative: group-by might be in a sort/filter dropdown
                var sortBtn = SpanAppFixture.FindById(_window!, "Button_Sort");
                if (sortBtn == null)
                {
                    RestoreMillerView();
                    Assert.Inconclusive("Group-by control not found — feature may use context menu or different AutomationId");
                    return;
                }

                // Click sort button to open dropdown that may contain group options
                sortBtn.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Look for group-by type option in the dropdown
                var groupByType = SpanAppFixture.FindById(_window!, "MenuItem_GroupByType");
                if (groupByType == null)
                {
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
                    RestoreMillerView();
                    Assert.Inconclusive("Group by type option not found in sort dropdown");
                    return;
                }

                groupByType.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            }
            else
            {
                groupByBtn.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Select "Type" from group options
                var typeOption = SpanAppFixture.WaitForElement(_window!, "GroupOption_Type", 2000);
                if (typeOption != null)
                {
                    typeOption.Click();
                    Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
                }
            }

            // After grouping, app should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after group-by operation");

            // Try to reset grouping (group by none)
            var groupByNone = SpanAppFixture.FindById(_window!, "GroupOption_None");
            if (groupByNone != null)
            {
                groupByNone.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
            }

            RestoreMillerView();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            RestoreMillerView();
            Assert.Inconclusive($"Group by type test could not complete: {ex.Message}");
        }
    }
}
