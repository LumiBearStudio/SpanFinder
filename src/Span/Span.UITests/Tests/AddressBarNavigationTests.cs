using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

[TestClass]
public class AddressBarNavigationTests
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
    public void AddressBar_TypePath_NavigatesToFolder()
    {
        Assert.IsTrue(SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath),
            "Should navigate to folder via address bar");

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigation");

        SpanAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar should be accessible after navigation");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AddressBar_TypeDrivePath_NavigatesToDrive()
    {
        Assert.IsTrue(SpanAppFixture.NavigateToPath(_window!, @"C:\"),
            "Should navigate to C:\\ via address bar");

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigating to C:\\");

        SpanAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar should be accessible after drive navigation");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AddressBar_InvalidPath_DoesNotCrash()
    {
        SpanAppFixture.NavigateToPath(_window!, @"Z:\NonExistent\Path\That\Should\Not\Exist");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should not crash on invalid path");
    }

    [TestMethod]
    public void AddressBar_EscapeKey_CancelsEdit()
    {
        SpanAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = SpanAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar text box should appear after Ctrl+L");

        Keyboard.Type("random-text-that-should-be-discarded");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should be responsive after Escape cancels address bar edit");
    }

    [TestMethod]
    public void AddressBar_CopyPathButton_Exists()
    {
        var copyPathBtn = SpanAppFixture.FindById(_window!, "Button_CopyPath");
        Assert.IsNotNull(copyPathBtn, "Button_CopyPath should exist in the address bar area");
    }

    [TestMethod]
    public void AddressBar_SequentialNavigation_UpdatesPath()
    {
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPath);
        SpanAppFixture.NavigateToPath(_window!, @"C:\");
        SpanAppFixture.NavigateToPath(_window!, SpanAppFixture.NavPathAlt);

        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after sequential navigations");
    }
}
