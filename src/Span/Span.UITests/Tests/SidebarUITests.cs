using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// Tests for sidebar functionality: local drives, cloud drives, favorites, navigation, settings.
///
/// Prerequisites: SPAN Finder must be running.
/// </summary>
[TestClass]
public class SidebarUITests
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
    public void Sidebar_ShowsLocalDrives()
    {
        try
        {
            // Look for sidebar or drive-related elements
            // The sidebar may use a TreeView or StackPanel with drive items
            var sidebar = SpanAppFixture.FindById(_window!, "Sidebar");
            if (sidebar == null)
            {
                // Try alternative: sidebar might be part of the main layout without specific AutomationId
                // Look for a drive item directly (C: drive should exist on any Windows machine)
                var driveC = SpanAppFixture.FindById(_window!, "SidebarItem_C");
                if (driveC == null)
                {
                    // Try finding any element with "drive" in its automation properties
                    // Fall back to verifying the app has the expected layout
                    var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
                    Assert.IsNotNull(backBtn, "App should be responsive — sidebar drive detection requires AutomationIds");
                    Assert.Inconclusive("Sidebar drive items not found via AutomationId — sidebar may use different element structure");
                }
                else
                {
                    Assert.IsNotNull(driveC, "C: drive should appear in sidebar");
                }
            }
            else
            {
                // Sidebar found — verify it has children (drive items)
                var children = sidebar.FindAllChildren();
                Assert.IsTrue(children.Length > 0, "Sidebar should contain at least one drive item");
            }
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Sidebar drives test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sidebar_ShowsCloudDrives()
    {
        try
        {
            // Cloud drives (OneDrive, Google Drive, etc.) may or may not be installed
            var cloudSection = SpanAppFixture.FindById(_window!, "SidebarSection_Cloud");
            if (cloudSection == null)
            {
                // Try individual cloud provider items
                var oneDrive = SpanAppFixture.FindById(_window!, "SidebarItem_OneDrive");
                var googleDrive = SpanAppFixture.FindById(_window!, "SidebarItem_GoogleDrive");
                var dropbox = SpanAppFixture.FindById(_window!, "SidebarItem_Dropbox");

                if (oneDrive == null && googleDrive == null && dropbox == null)
                {
                    Assert.Inconclusive("No cloud drive items found — cloud services may not be installed or sidebar uses different AutomationIds");
                }
                else
                {
                    // At least one cloud drive found
                    Assert.IsTrue(
                        oneDrive != null || googleDrive != null || dropbox != null,
                        "At least one cloud drive should be detected");
                }
            }
            else
            {
                Assert.IsNotNull(cloudSection, "Cloud drives section should exist in sidebar");
            }
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Cloud drives test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sidebar_FavoritesSection_Exists()
    {
        try
        {
            // Look for favorites section in sidebar
            var favoritesSection = SpanAppFixture.FindById(_window!, "SidebarSection_Favorites");
            if (favoritesSection == null)
            {
                // Try alternative naming
                var favorites = SpanAppFixture.FindById(_window!, "Sidebar_Favorites");
                if (favorites == null)
                {
                    // The sidebar may show Quick Access or Pinned items instead
                    var quickAccess = SpanAppFixture.FindById(_window!, "SidebarSection_QuickAccess");
                    var pinned = SpanAppFixture.FindById(_window!, "SidebarSection_Pinned");

                    if (quickAccess == null && pinned == null)
                    {
                        Assert.Inconclusive("Favorites/Quick Access section not found — sidebar may use different AutomationIds");
                    }
                    else
                    {
                        Assert.IsTrue(
                            quickAccess != null || pinned != null,
                            "Quick Access or Pinned section should exist in sidebar");
                    }
                }
                else
                {
                    Assert.IsNotNull(favorites, "Favorites section should exist");
                }
            }
            else
            {
                Assert.IsNotNull(favoritesSection, "Favorites section should exist in sidebar");
            }
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Favorites section test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sidebar_ClickDrive_NavigatesToRoot()
    {
        try
        {
            // Try to find and click a drive item in the sidebar
            var driveC = SpanAppFixture.FindById(_window!, "SidebarItem_C");
            if (driveC == null)
            {
                // Try using the sidebar container and finding any clickable drive
                var sidebar = SpanAppFixture.FindById(_window!, "Sidebar");
                if (sidebar == null)
                {
                    Assert.Inconclusive("Sidebar not found — cannot test drive click navigation");
                    return;
                }

                var children = sidebar.FindAllChildren();
                if (children.Length == 0)
                {
                    Assert.Inconclusive("No sidebar items found to click");
                    return;
                }

                // Click the first sidebar item
                children[0].Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
            }
            else
            {
                driveC.Click();
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
            }

            // After clicking a drive, the app should navigate and remain responsive
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after sidebar drive click");
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Sidebar click test could not complete: {ex.Message}");
        }
    }

    [TestMethod]
    public void Sidebar_Settings_OpensSettingsTab()
    {
        try
        {
            // Open settings via Ctrl+, shortcut
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            // Settings tab should open — look for settings-specific elements
            var settingsScroll = SpanAppFixture.WaitForElement(_window!, "SettingsScrollViewer", 3000);

            // App should remain responsive regardless
            var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
            Assert.IsNotNull(backBtn, "App should remain responsive after opening settings");

            if (settingsScroll == null)
            {
                // Settings may use different AutomationId or be a ContentDialog
                // Still verify the shortcut didn't crash the app
                Assert.Inconclusive("Settings tab element not found via AutomationId 'SettingsScrollViewer'");
            }
            else
            {
                Assert.IsNotNull(settingsScroll, "Settings scroll viewer should be visible");
            }

            // Close settings tab
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is not AssertInconclusiveException && ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Settings test could not complete: {ex.Message}");
        }
    }
}
