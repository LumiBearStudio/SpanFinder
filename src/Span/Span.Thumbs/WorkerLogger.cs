using System;
using System.IO;

namespace Span.Thumbs;

/// <summary>
/// 워커 전용 로그. 메인 DebugLogger와 같은 폴더에 별도 prefix로 저장.
/// 위치: %LocalAppData%\Span\Logs\Span_Worker_yyyyMMdd_HHmmss_pid{pid}.log
///
/// 동기 append만 사용 — 워커는 단순 단일 스레드 처리에 가까워 channel 불필요.
/// 7일 이전 워커 로그는 메인 측 DebugLogger의 LRU 정리에서 함께 처리됨 (file pattern).
/// </summary>
internal static class WorkerLogger
{
    private static readonly object _lock = new();
    private static readonly string LogPath;

    static WorkerLogger()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logsDir = Path.Combine(baseDir, "Span", "Logs");
        try { Directory.CreateDirectory(logsDir); } catch { }
        var sessionTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var pid = Environment.ProcessId;
        LogPath = Path.Combine(logsDir, $"Span_Worker_{sessionTag}_pid{pid}.log");
        try
        {
            File.WriteAllText(LogPath, $"=== Span Thumbs Worker Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid={pid}) ===\n\n");
        }
        catch { }
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.Error.WriteLine(line);  // 메인이 stderr 캡처 가능
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath, line + "\n");
            }
        }
        catch { /* ignore */ }
    }
}
