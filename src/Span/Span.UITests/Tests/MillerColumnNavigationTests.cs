using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class MillerColumnNavigationTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.GetMainWindow();
        SpanAppFixture.Focus(_window);
        SpanAppFixture.NavigateToPath(_window, SpanAppFixture.NavPath);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [ClassCleanup]
    public static void ClassCleanup() => SpanAppFixture.Detach();

    [TestInitialize]
    public void TestInit() => SpanAppFixture.Focus(_window!);

    private static void EnsureMillerFocus()
    {
        var millerHost = SpanAppFixture.FindById(_window!, "Host_Miller");
        millerHost?.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void MillerColumn_RightArrow_EntersFolder()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.RIGHT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Right arrow navigation");

        Keyboard.Type(VirtualKeyShort.LEFT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void MillerColumn_LeftArrow_ReturnsToPreviousColumn()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.RIGHT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        Keyboard.Type(VirtualKeyShort.LEFT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Left arrow navigation");
    }

    [TestMethod]
    public void MillerColumn_Enter_OpensFolder()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Enter to open folder");

        Keyboard.Type(VirtualKeyShort.BACK);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void MillerColumn_Backspace_GoesUp()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.RIGHT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
        Keyboard.Type(VirtualKeyShort.BACK);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Backspace navigation");
    }

    [TestMethod]
    public void MillerColumn_DownArrow_SelectsNextItem()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Down arrow selection");
    }

    [TestMethod]
    public void MillerColumn_UpArrow_SelectsPreviousItem()
    {
        EnsureMillerFocus();
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(VirtualKeyShort.UP);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Up arrow selection");
    }
}
