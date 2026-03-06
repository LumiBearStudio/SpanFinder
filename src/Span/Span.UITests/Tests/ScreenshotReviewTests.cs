using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.IO;

namespace Span.UITests.Tests;

/// <summary>
/// Screenshot-based UI review tests.
/// Captures screenshots of key screens per language for visual analysis.
///
/// Usage:
///   1. Launch SPAN Finder (F5)
///   2. Run: dotnet test src/Span/Span.UITests/Span.UITests.csproj -p:Platform=x64 --filter "ScreenshotReviewTests"
///   3. Screenshots saved to: Span.UITests/TestResults/Screenshots/{lang}/
///   4. Run analyzer: dotnet run --project src/Span/Span.ScreenshotAnalyzer [screenshots-dir]
/// </summary>
[TestClass]
public class ScreenshotReviewTests
{
    private static readonly string ScreenshotDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestResults", "Screenshots");

    // Language ComboBox index → language code
    private static readonly (int index, string code, string name)[] Languages =
    {
        (1, "en", "English"),
        (2, "ko", "Korean"),
        (6, "de", "Deutsch"),
        (8, "fr", "Francais"),
        (9, "pt-BR", "Portuguese"),
    };

    // Settings nav items: x:Name values for NavigationViewItem
    private static readonly string[] SettingsNavItems =
        { "NavGeneral", "NavAppearance", "NavBrowsing", "NavTools", "NavAdvanced", "NavAbout", "NavOpenSource" };

    [TestMethod]
    public void CaptureAllLanguages()
    {
        var window = SpanAppFixture.GetMainWindow();
        SpanAppFixture.Focus(window);

        SpanAppFixture.NavigateToPath(window, @"C:\Windows");
        Thread.Sleep(1500);

        foreach (var (index, code, name) in Languages)
        {
            Console.WriteLine($"--- Capturing: {name} ({code}) ---");
            CaptureLanguage(window, index, code);
            Console.WriteLine($"  Done: {code}");
        }

        // Restore to Korean
        RestoreLanguage(window, 2);
        Console.WriteLine($"\nAll screenshots saved to: {ScreenshotDir}");
    }

    [TestMethod]
    public void CaptureGermanOnly()
    {
        var window = SpanAppFixture.GetMainWindow();
        SpanAppFixture.Focus(window);

        SpanAppFixture.NavigateToPath(window, @"C:\Windows");
        Thread.Sleep(1500);

        CaptureLanguage(window, 6, "de");
        RestoreLanguage(window, 2);

        Console.WriteLine($"German screenshots saved to: {Path.Combine(ScreenshotDir, "de")}");
    }

    // ── Core capture flow ──

    private void CaptureLanguage(Window window, int langIndex, string langCode)
    {
        // Switch language
        SwitchLanguage(window, langIndex);
        Thread.Sleep(1000);

        // Close settings → back to explorer
        CloseSettingsTab(window);
        Thread.Sleep(800);

        var langDir = Path.Combine(ScreenshotDir, langCode);
        Directory.CreateDirectory(langDir);

        int n = 1;

        // 1) Full window — sidebar + miller columns
        CaptureWindow(window, Path.Combine(langDir, $"{n++:D2}_full_window.png"));

        // 2) Context menu — right-click a folder
        CaptureContextMenu(window, Path.Combine(langDir, $"{n++:D2}_context_menu.png"));

        // 3-N) Settings pages — one per nav section
        OpenSettings(window);
        Thread.Sleep(800);

        foreach (var navName in SettingsNavItems)
        {
            var nav = SpanAppFixture.FindById(window, navName);
            if (nav == null)
            {
                Console.WriteLine($"    Nav item '{navName}' not found, skipping");
                continue;
            }

            nav.Click();
            Thread.Sleep(600);

            var label = navName.Replace("Nav", "").ToLower();
            CaptureWindow(window, Path.Combine(langDir, $"{n++:D2}_settings_{label}.png"));

            // For long sections, scroll down and capture again
            if (navName is "NavGeneral" or "NavAppearance" or "NavAdvanced")
            {
                ScrollDown(window, 5);
                Thread.Sleep(400);
                CaptureWindow(window, Path.Combine(langDir, $"{n++:D2}_settings_{label}_scrolled.png"));
            }
        }

        // Close settings for next iteration
        CloseSettingsTab(window);
        Thread.Sleep(500);

        Console.WriteLine($"    Saved {n - 1} screenshots to {langDir}");
    }

    private static void RestoreLanguage(Window window, int langIndex)
    {
        SwitchLanguage(window, langIndex);
        Thread.Sleep(500);
        CloseSettingsTab(window);
    }

    // ── Helpers ──

    private static void SwitchLanguage(Window window, int comboIndex)
    {
        OpenSettings(window);
        Thread.Sleep(800);

        // Ensure General tab is selected (language combo is there)
        var navGeneral = SpanAppFixture.FindById(window, "NavGeneral");
        navGeneral?.Click();
        Thread.Sleep(400);

        var combo = SpanAppFixture.WaitForElement(window, "LanguageCombo", 3000);
        if (combo == null)
        {
            Console.WriteLine("  WARNING: LanguageCombo not found");
            return;
        }

        var comboBox = combo.AsComboBox();
        comboBox.Select(comboIndex);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    private static void OpenSettings(Window window)
    {
        SpanAppFixture.Focus(window);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    private static void CloseSettingsTab(Window window)
    {
        SpanAppFixture.Focus(window);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    private static void ScrollDown(Window window, int clicks)
    {
        // Use mouse wheel scrolling — more reliable than Page Down in WinUI 3
        SpanAppFixture.Focus(window);

        // Move mouse to center of window content area
        var bounds = window.BoundingRectangle;
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        Mouse.MoveTo(centerX, centerY);
        Thread.Sleep(100);

        for (int i = 0; i < clicks; i++)
        {
            Mouse.Scroll(-3); // negative = scroll down
            Thread.Sleep(150);
        }
    }

    private static void CaptureWindow(Window window, string filePath)
    {
        using var capture = Capture.Element(window);
        capture.ToFile(filePath);
    }

    private static void CaptureContextMenu(Window window, string filePath)
    {
        SpanAppFixture.Focus(window);
        Thread.Sleep(300);

        AutomationElement? target = null;

        // Find ListItems in the window (works for all view modes)
        var allListItems = window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));

        if (allListItems.Length > 2)
            target = allListItems[2]; // Skip ".." and first selected
        else if (allListItems.Length > 0)
            target = allListItems[0];

        if (target != null)
        {
            Console.WriteLine($"    Context menu target: '{SpanAppFixture.SafeGetName(target)}'");

            // Click to select, then right-click
            target.Click();
            Thread.Sleep(300);
            target.RightClick();
            Thread.Sleep(1200); // Context menu + shell extension loading

            // Capture the app window region (includes popup menu if overlapping)
            // Plus extra margin for context menu that extends beyond window bounds
            var winBounds = window.BoundingRectangle;
            var captureRect = new System.Drawing.Rectangle(
                winBounds.Left - 50,
                winBounds.Top,
                winBounds.Width + 100,
                winBounds.Height + 50);

            // Clamp to screen bounds
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(winBounds.Left, winBounds.Top));
            captureRect.Intersect(screen.Bounds);

            using var capture = Capture.Rectangle(captureRect);
            capture.ToFile(filePath);

            // Dismiss menu
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Thread.Sleep(300);
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Thread.Sleep(200);
        }
        else
        {
            Console.WriteLine("    WARNING: No list items found for context menu capture");
            CaptureWindow(window, filePath);
        }
    }
}
