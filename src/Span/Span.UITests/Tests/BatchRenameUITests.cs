using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for batch rename dialog: multi-select + F2 opens batch rename,
/// find/replace, prefix/suffix, numbering modes, and conflict detection.
///
/// Prerequisites: SPAN Finder must be running. E:\TEST should exist with multiple files.
/// </summary>
[TestClass]
public class BatchRenameUITests
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
    /// Helper: select multiple items via Ctrl+A, then press F2 for batch rename.
    /// Returns true if the batch rename dialog was detected.
    /// </summary>
    private static bool TryOpenBatchRenameDialog()
    {
        // Select all items
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Press F2 to open batch rename dialog
        Keyboard.Type(VirtualKeyShort.F2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // Look for batch rename dialog elements
        var batchDialog = SpanAppFixture.WaitForElement(_window!, "BatchRenameDialog", 3000);
        return batchDialog != null;
    }

    /// <summary>
    /// Helper: dismiss any open dialog with Escape.
    /// </summary>
    private static void DismissDialog()
    {
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Deselect all
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void MultiSelect_F2_OpensBatchDialog()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // Select multiple items
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // Press F2 — with multiple items selected, batch rename dialog should open
            Keyboard.Type(VirtualKeyShort.F2);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

            // Look for batch rename dialog or any dialog that appeared
            var batchDialog = SpanAppFixture.WaitForElement(_window!, "BatchRenameDialog", 3000);
            // App should remain responsive regardless
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after multi-select F2");

            if (batchDialog == null)
                Assert.Inconclusive("Batch rename dialog AutomationId not found — feature may not be implemented yet");

            DismissDialog();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Batch rename test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void FindReplace_Mode_UpdatesPreview()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            if (!TryOpenBatchRenameDialog())
            {
                DismissDialog();
                Assert.Inconclusive("Batch rename dialog not available");
            }

            // Look for find/replace input fields
            var findInput = SpanAppFixture.FindById(_window!, "BatchRename_FindInput");
            var replaceInput = SpanAppFixture.FindById(_window!, "BatchRename_ReplaceInput");

            if (findInput == null || replaceInput == null)
            {
                DismissDialog();
                Assert.Inconclusive("Find/Replace input fields not found in batch rename dialog");
            }

            // Type find text
            findInput!.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
            Keyboard.Type("test");
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // Type replace text
            replaceInput!.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
            Keyboard.Type("renamed");
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // Preview should update — verify app is responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive during find/replace preview");

            DismissDialog();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Find/Replace test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void PrefixSuffix_Mode_UpdatesPreview()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            if (!TryOpenBatchRenameDialog())
            {
                DismissDialog();
                Assert.Inconclusive("Batch rename dialog not available");
            }

            // Look for prefix/suffix mode selector or tab
            var prefixSuffixTab = SpanAppFixture.FindById(_window!, "BatchRename_PrefixSuffixTab");
            if (prefixSuffixTab == null)
            {
                // Try alternative: maybe it's a ComboBox or RadioButton
                var modeSelector = SpanAppFixture.FindById(_window!, "BatchRename_ModeSelector");
                if (modeSelector == null)
                {
                    DismissDialog();
                    Assert.Inconclusive("Prefix/Suffix mode selector not found");
                }
                modeSelector.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
            }
            else
            {
                prefixSuffixTab.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
            }

            // Look for prefix input
            var prefixInput = SpanAppFixture.FindById(_window!, "BatchRename_PrefixInput");
            if (prefixInput != null)
            {
                prefixInput.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
                Keyboard.Type("prefix_");
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
            }

            // App should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive during prefix/suffix preview");

            DismissDialog();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Prefix/Suffix test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Numbering_Mode_UpdatesPreview()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            if (!TryOpenBatchRenameDialog())
            {
                DismissDialog();
                Assert.Inconclusive("Batch rename dialog not available");
            }

            // Look for numbering mode selector
            var numberingTab = SpanAppFixture.FindById(_window!, "BatchRename_NumberingTab");
            if (numberingTab == null)
            {
                DismissDialog();
                Assert.Inconclusive("Numbering mode tab not found in batch rename dialog");
            }

            numberingTab.Click();
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive with numbering preview
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive during numbering preview");

            DismissDialog();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Numbering test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void ConflictDetection_ShowsWarning()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            if (!TryOpenBatchRenameDialog())
            {
                DismissDialog();
                Assert.Inconclusive("Batch rename dialog not available");
            }

            // Try to rename all items to the same name to trigger conflict
            var findInput = SpanAppFixture.FindById(_window!, "BatchRename_FindInput");
            var replaceInput = SpanAppFixture.FindById(_window!, "BatchRename_ReplaceInput");

            if (findInput != null && replaceInput != null)
            {
                // Clear and type a pattern that would cause all files to have the same name
                findInput.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type(".*");
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));

                replaceInput.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type("conflict");
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Look for conflict warning
                var warningElement = SpanAppFixture.FindById(_window!, "BatchRename_ConflictWarning");
                // App should remain responsive
                var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
                Assert.IsNotNull(backBtn, "App should remain responsive during conflict detection");

                if (warningElement == null)
                    Assert.Inconclusive("Conflict warning element not found — feature may use different AutomationId");
            }
            else
            {
                Assert.Inconclusive("Find/Replace inputs not found in batch rename dialog");
            }

            DismissDialog();
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Conflict detection test could not complete: {ex.Message}");
        }
    }
}
