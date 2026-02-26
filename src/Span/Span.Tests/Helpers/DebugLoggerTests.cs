using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class DebugLoggerTests
{
    [TestMethod]
    public void LogFilePath_IsUnderLogsFolder()
    {
        var logPath = DebugLogger.GetLogFilePath();

        Assert.IsNotNull(logPath);
        Assert.IsTrue(logPath.Contains(Path.DirectorySeparatorChar + "Logs" + Path.DirectorySeparatorChar)
                    || logPath.Contains("/Logs/"),
            $"Log path should be under Logs/ folder, but was: {logPath}");
    }

    [TestMethod]
    public void LogFilePath_EndsWithSpanDebugLog()
    {
        var logPath = DebugLogger.GetLogFilePath();

        Assert.IsTrue(logPath.EndsWith("Span_Debug.log"),
            $"Log path should end with Span_Debug.log, but was: {logPath}");
    }

    [TestMethod]
    public void LogFilePath_IsNotOnDesktop()
    {
        var logPath = DebugLogger.GetLogFilePath();
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        Assert.IsFalse(logPath.StartsWith(desktopPath, StringComparison.OrdinalIgnoreCase),
            $"Log path should NOT be on Desktop, but was: {logPath}");
    }

    [TestMethod]
    public void LogFilePath_IsAbsolutePath()
    {
        var logPath = DebugLogger.GetLogFilePath();

        Assert.IsTrue(Path.IsPathRooted(logPath),
            $"Log path should be absolute, but was: {logPath}");
    }

    [TestMethod]
    public void Log_DoesNotThrow()
    {
        // Basic smoke test — logging should never crash the app
        DebugLogger.Log("Unit test log message");
    }

    [TestMethod]
    public void LogCrash_DoesNotThrow()
    {
        var ex = new InvalidOperationException("Test exception");
        DebugLogger.LogCrash("UnitTest", ex);
    }

    [TestMethod]
    public void LogCrash_NullException_DoesNotThrow()
    {
        DebugLogger.LogCrash("UnitTest", null);
    }
}
