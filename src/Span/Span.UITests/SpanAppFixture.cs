using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;

namespace Span.UITests;

/// <summary>
/// Shared fixture that manages FlaUI automation handle to the Span application.
///
/// WinUI 3 apps require MSIX packaging and cannot be launched via Application.Launch().
/// Tests attach to an already-running Span process.
///
/// Usage:
///   1. Launch Span from Visual Studio (F5) or Start Menu
///   2. Run tests: dotnet test src/Span/Span.UITests/Span.UITests.csproj -p:Platform=x64
/// </summary>
public static class SpanAppFixture
{
    private static Application? _app;
    private static UIA3Automation? _automation;
    private static Window? _cachedWindow;

    /// <summary>
    /// Attach to the running Span process and return its main window.
    /// Caches the window handle across calls within the same test session.
    /// </summary>
    public static Window GetMainWindow(int timeoutSeconds = 10)
    {
        if (_cachedWindow != null)
        {
            try
            {
                // Verify window is still valid
                _ = _cachedWindow.Title;
                return _cachedWindow;
            }
            catch
            {
                _cachedWindow = null;
            }
        }

        _automation ??= new UIA3Automation();

        if (_app == null || _app.HasExited)
        {
            var processes = Process.GetProcessesByName("Span");
            if (processes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No running Span process found.\n" +
                    "WinUI 3 apps require MSIX packaging — launch Span from Visual Studio (F5) or Start Menu first.");
            }

            _app = Application.Attach(processes[0]);
        }

        var mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(timeoutSeconds));
        if (mainWindow == null)
            throw new TimeoutException($"Span main window did not appear within {timeoutSeconds}s");

        _cachedWindow = mainWindow;
        return mainWindow;
    }

    /// <summary>
    /// Detach from the application (does NOT close it).
    /// Tests should not close the app — the user controls the app lifecycle.
    /// </summary>
    public static void Detach()
    {
        _cachedWindow = null;
        // Do NOT call _app.Close() — we're just detaching, not closing
        _app?.Dispose();
        _automation?.Dispose();
        _app = null;
        _automation = null;
    }

    /// <summary>
    /// Find element by AutomationId in the given parent.
    /// </summary>
    public static AutomationElement? FindById(AutomationElement parent, string automationId)
    {
        return parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
    }

    /// <summary>
    /// Find element by AutomationId, throwing if not found.
    /// </summary>
    public static AutomationElement FindByIdOrThrow(AutomationElement parent, string automationId)
    {
        return parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            ?? throw new InvalidOperationException($"Element with AutomationId '{automationId}' not found");
    }

    /// <summary>
    /// Poll for an element by AutomationId until it appears or timeout.
    /// Useful for elements that become visible after UI transitions (e.g., Collapsed → Visible).
    /// </summary>
    public static AutomationElement? WaitForElement(AutomationElement parent, string automationId, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var el = FindById(parent, automationId);
            if (el != null) return el;
            Thread.Sleep(100);
        }
        return null;
    }
}
