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
    public void LogFilePath_HasSessionTimestampPattern()
    {
        // 931e2d3에서 세션별 파일명으로 변경: Span_Debug_yyyyMMdd_HHmmss.log
        var logPath = DebugLogger.GetLogFilePath();
        var fileName = Path.GetFileName(logPath);

        Assert.IsTrue(fileName.StartsWith("Span_Debug_"),
            $"Log filename should start with 'Span_Debug_', but was: {fileName}");
        Assert.IsTrue(fileName.EndsWith(".log"),
            $"Log filename should end with '.log', but was: {fileName}");
        // yyyyMMdd_HHmmss = 15자 + prefix(11) + suffix(4) = 30자
        Assert.AreEqual(30, fileName.Length,
            $"Filename length mismatch (expected 30 = 11 prefix + 15 timestamp + 4 suffix): {fileName}");
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

    // ── Phase 0 — 비정상 종료 감지 인프라 ──

    [TestMethod]
    public void GetPreviousSessionLogPath_DoesNotReturnCurrentSession()
    {
        // 현재 세션 파일은 절대 반환되면 안 됨 (자기 자신을 post-mortem 분석하면 무한 루프)
        var current = DebugLogger.GetLogFilePath();
        var prev = DebugLogger.GetPreviousSessionLogPath();

        if (prev != null)
            Assert.AreNotEqual(current, prev,
                "GetPreviousSessionLogPath must not return current session log");
    }

    [TestMethod]
    public void GetPreviousSessionLogPath_NeverThrows()
    {
        // 파일 시스템 문제(권한/디스크 가득 등)에서도 null 반환만, throw 안 함
        var prev = DebugLogger.GetPreviousSessionLogPath();
        // null 또는 string — throw만 안 하면 통과
    }

    [TestMethod]
    public void Shutdown_DoesNotThrow_AndIsIdempotent()
    {
        // 정상 종료 마커 기록 — 이미 channel이 complete인 상태에서도 throw 안 함
        DebugLogger.Shutdown();
        // 두 번째 호출도 안전 (App.UnregisterWindow에서 한 번, finalizer에서 한 번 가능)
        DebugLogger.Shutdown();
    }
}
