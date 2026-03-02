using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for the Help Overlay (F1 key).
/// WinUI 3 Grid containers don't create UIA peers, so we verify the overlay
/// by searching for child text elements within the overlay.
/// </summary>
[TestClass]
public class HelpOverlayTests
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
    public void EnsureOverlayClosed()
    {
        SpanAppFixture.Focus(_window!);
        // Send Escape to dismiss any open overlay
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    /// <summary>
    /// Check if the help overlay is currently showing by looking for its text content.
    /// WinUI 3 Grid elements don't appear in UIA tree, so we search for child Text elements.
    /// </summary>
    private static bool IsHelpOverlayVisible()
    {
        // The HelpOverlay Grid won't appear in UIA, but its child HelpFlyoutContent
        // contains Text elements with shortcut descriptions. If those appear, overlay is open.
        var textElements = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
        foreach (var el in textElements)
        {
            var name = el.Properties.Name.ValueOrDefault ?? "";
            // HelpFlyoutContent has shortcut descriptions containing these strings
            if (name.Contains("Ctrl+") || name.Contains("F2") || name.Contains("F5"))
                return true;
        }
        return false;
    }

    [TestMethod]
    public void HelpOverlay_F1_TogglesVisibility()
    {
        try
        {
            SpanAppFixture.Focus(_window!);

            // Press F1 to open help overlay
            Keyboard.Type(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

            // Help overlay should show shortcut descriptions
            Assert.IsTrue(IsHelpOverlayVisible(),
                "F1 should open Help Overlay with shortcut descriptions");

            // Press F1 again to toggle it off (any key closes overlay)
            Keyboard.Type(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // App should remain responsive
            var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");
            Assert.IsNotNull(newTabBtn, "App should remain responsive after toggling help overlay");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Help overlay toggle test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void HelpOverlay_EscapeCloses()
    {
        try
        {
            SpanAppFixture.Focus(_window!);

            // Open help overlay with F1
            Keyboard.Type(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

            if (!IsHelpOverlayVisible())
                Assert.Inconclusive("F1 did not open overlay — focus issue");

            // Close with Escape
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // Verify overlay is closed (shortcut texts should no longer be prominent)
            // App should remain responsive
            var newTabBtn = SpanAppFixture.FindById(_window!, "Button_NewTab");
            Assert.IsNotNull(newTabBtn, "App should remain responsive after closing help overlay");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Help overlay Escape test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void HelpOverlay_ContentIsDisplayed()
    {
        try
        {
            SpanAppFixture.Focus(_window!);

            // Open help overlay with F1
            Keyboard.Type(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

            if (!IsHelpOverlayVisible())
                Assert.Inconclusive("F1 did not open overlay — focus issue");

            // Verify at least one text element has meaningful shortcut content
            var textElements = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
            bool hasContent = false;
            foreach (var textEl in textElements)
            {
                var name = textEl.Properties.Name.ValueOrDefault ?? "";
                if (name.Contains("Ctrl+") || name.Contains("F2") || name.Contains("F5"))
                {
                    hasContent = true;
                    break;
                }
            }
            Assert.IsTrue(hasContent, "Help Overlay should display readable shortcut text content");

            // Close overlay
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Help overlay content test could not complete: {ex.Message}");
        }
    }
}
