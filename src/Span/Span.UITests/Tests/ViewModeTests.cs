using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for view mode switching (Miller, Details, List, Icon).
/// </summary>
[TestClass]
public class ViewModeTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.LaunchOrAttach();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SpanAppFixture.Close();
    }

    [TestMethod]
    public void ViewModeButton_Exists_And_Clickable()
    {
        var viewModeBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_ViewMode");
        Assert.IsTrue(viewModeBtn.IsEnabled, "View mode button should be enabled");
    }

    [TestMethod]
    public void Shortcut_Ctrl1_SwitchesToMiller()
    {
        // Ctrl+1 = Miller Columns
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var millerHost = SpanAppFixture.FindById(_window!, "Host_Miller");
        Assert.IsNotNull(millerHost, "Miller host should exist after Ctrl+1");
    }

    [TestMethod]
    public void Shortcut_Ctrl2_SwitchesToDetails()
    {
        // Ctrl+2 = Details
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var detailsHost = SpanAppFixture.FindById(_window!, "Host_Details");
        Assert.IsNotNull(detailsHost, "Details host should exist after Ctrl+2");

        // Switch back to Miller
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
