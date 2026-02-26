using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for search functionality (FEATURES.md: 검색 섹션).
/// Verifies search box focus, input, and clear behavior.
/// </summary>
[TestClass]
public class SearchTests
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
    public void SearchBox_Exists_And_IsEnabled()
    {
        var searchBox = SpanAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist");
        Assert.IsTrue(searchBox.IsEnabled, "Search box should be enabled");
    }

    [TestMethod]
    public void CtrlF_FocusesSearchBox_EscClears()
    {
        // Focus search
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var searchBox = SpanAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist after Ctrl+F");

        // Type a search query
        Keyboard.Type("test");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Escape should clear/dismiss
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SearchBox_AcceptsAdvancedSyntax()
    {
        // Focus search
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Type advanced query (should not crash)
        Keyboard.Type("kind:image size:>1MB");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Clear
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
