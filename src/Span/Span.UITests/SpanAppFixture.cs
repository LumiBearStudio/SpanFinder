using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;

namespace Span.UITests;

/// <summary>
/// Shared fixture that launches and manages the Span application process.
/// Tests use this to get a FlaUI automation handle to the main window.
/// </summary>
public static class SpanAppFixture
{
    private static Application? _app;
    private static UIA3Automation? _automation;

    /// <summary>
    /// Path to the Span executable. Override via SPAN_EXE_PATH environment variable.
    /// </summary>
    public static string ExePath =>
        Environment.GetEnvironmentVariable("SPAN_EXE_PATH")
        ?? FindExePath();

    /// <summary>
    /// Launch Span and return the main window automation element.
    /// If already running, returns the existing window.
    /// </summary>
    public static Window LaunchOrAttach(int timeoutSeconds = 15)
    {
        _automation ??= new UIA3Automation();

        if (_app == null || _app.HasExited)
        {
            var exePath = ExePath;
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"Span.exe not found at: {exePath}");

            _app = Application.Launch(exePath);
        }

        var mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(timeoutSeconds));
        if (mainWindow == null)
            throw new TimeoutException($"Span main window did not appear within {timeoutSeconds}s");

        return mainWindow;
    }

    /// <summary>
    /// Attach to an already-running Span process (useful for debugging).
    /// </summary>
    public static Window AttachToRunning(int timeoutSeconds = 10)
    {
        _automation ??= new UIA3Automation();

        var processes = Process.GetProcessesByName("Span");
        if (processes.Length == 0)
            throw new InvalidOperationException("No running Span process found. Launch the app first.");

        _app = Application.Attach(processes[0]);
        var mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(timeoutSeconds));
        if (mainWindow == null)
            throw new TimeoutException("Could not find Span main window");

        return mainWindow;
    }

    /// <summary>
    /// Close the application gracefully.
    /// </summary>
    public static void Close()
    {
        try
        {
            _app?.Close();
        }
        catch { }
        finally
        {
            _app?.Dispose();
            _automation?.Dispose();
            _app = null;
            _automation = null;
        }
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

    private static string FindExePath()
    {
        // Walk up from test bin dir to find the app executable
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "Span", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", "Span.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Span", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "Span.exe"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        // Fallback: absolute path
        return @"D:\11.AI\Span\src\Span\Span\bin\x64\Debug\net8.0-windows10.0.19041.0\Span.exe"!;
    }
}
