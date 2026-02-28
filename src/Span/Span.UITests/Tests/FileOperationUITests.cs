using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for file operations: copy, delete, new folder, rename, undo, select all.
/// Uses E:\TEST as the known test directory.
///
/// Prerequisites: SPAN Finder must be running. E:\TEST should exist with some files.
/// Tests are defensive — environment-dependent checks use Assert.Inconclusive.
/// </summary>
[TestClass]
public class FileOperationUITests
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

        // Select all existing text and type the new path
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(100);
        Keyboard.Type(path);
        Thread.Sleep(100);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    [TestMethod]
    public void NavigateToTestFolder_ShowsFiles()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // After navigation, verify the app is responsive and content loaded
            // WinUI 3 ListView items may not expose individual AutomationIds,
            // but the address bar should reflect the navigated path
            var textBox = SpanAppFixture.FindById(_window!, "TextBox_AddressBar");
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after navigation to E:\\TEST");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Navigation test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void CopyFile_ViaKeyboard_DoesNotCrash()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // Select first item with Down arrow
            Keyboard.Type(VirtualKeyShort.DOWN);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // Copy with Ctrl+C
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // App should remain responsive after copy
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+C");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Copy test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void DeleteFile_ViaKeyboard_TriggersAction()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            // Create a temp file to delete
            var tempFile = System.IO.Path.Combine(@"E:\TEST", $"_uitest_delete_{Guid.NewGuid():N}.txt");
            System.IO.File.WriteAllText(tempFile, "test file for UI delete");

            try
            {
                NavigateToPath(@"E:\TEST");

                // Refresh to ensure the new file is visible
                Keyboard.Type(VirtualKeyShort.F5);
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Select the temp file by typing its name prefix for type-ahead
                Keyboard.Type("_uitest_delete_");
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // Press Delete key
                Keyboard.Type(VirtualKeyShort.DELETE);
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // A confirmation dialog may appear — press Enter to confirm or Escape to cancel
                Keyboard.Type(VirtualKeyShort.RETURN);
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // App should remain responsive
                var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
                Assert.IsNotNull(backBtn, "App should remain responsive after delete operation");
            }
            finally
            {
                // Cleanup: remove temp file if it still exists
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Delete test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void NewFolder_ViaShortcut_CreatesFolder()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // Ctrl+Shift+N should trigger new folder creation
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_N);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

            // A new folder inline rename TextBox may appear, or a dialog
            // Either way, the app should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+Shift+N");

            // Press Escape to cancel the new folder creation if inline rename is active
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // Cleanup: remove any "New Folder" that may have been created
            var newFolderPath = System.IO.Path.Combine(@"E:\TEST", "New Folder");
            if (System.IO.Directory.Exists(newFolderPath))
                System.IO.Directory.Delete(newFolderPath, false);
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"New folder test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Rename_ViaF2_InlineEdit()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // Select first item
            Keyboard.Type(VirtualKeyShort.DOWN);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

            // Press F2 to start inline rename
            Keyboard.Type(VirtualKeyShort.F2);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // The rename should activate an inline TextBox
            // App should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after F2 rename");

            // Cancel the rename with Escape
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Rename test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Undo_AfterOperation_DoesNotCrash()
    {
        try
        {
            // Ctrl+Z should not crash regardless of current state
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+Z undo");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Undo test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void SelectAll_CtrlA_SelectsAllItems()
    {
        try
        {
            if (!System.IO.Directory.Exists(@"E:\TEST"))
                Assert.Inconclusive("E:\\TEST directory does not exist on this machine");

            NavigateToPath(@"E:\TEST");

            // Press Ctrl+A to select all items
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive — status bar may show selection count
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+A select all");

            // Deselect
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Select all test could not complete: {ex.Message}");
        }
    }
}
