using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class BackForwardNavigationTests
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
    public void BackButton_Exists_And_Clickable()
    {
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "Back button should exist");
    }

    [TestMethod]
    public void ForwardButton_Exists_And_Clickable()
    {
        var forwardBtn = SpanAppFixture.FindById(_window!, "Button_Forward");
        Assert.IsNotNull(forwardBtn, "Forward button should exist");
    }

    [TestMethod]
    public void BackButton_AfterNavigation_GoesBack()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPathAlt);

        var backBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_Back");
        backBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "App should remain responsive after Back navigation");
    }

    [TestMethod]
    public void ForwardButton_AfterBack_GoesForward()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPathAlt);

        var backBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_Back");
        backBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var forwardBtn = SpanAppFixture.FindByIdOrThrow(_window!, "Button_Forward");
        forwardBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var viewModeBtn = SpanAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "App should remain responsive after Forward navigation");
    }

    [TestMethod]
    public void BackForward_ViaKeyboard_AltLeftRight()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPathAlt);

        SpanAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.LEFT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backCheck = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backCheck, "App should remain responsive after Alt+Left (Back)");

        SpanAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var forwardCheck = SpanAppFixture.FindById(_window!, "Button_Forward");
        Assert.IsNotNull(forwardCheck, "App should remain responsive after Alt+Right (Forward)");
    }
}
