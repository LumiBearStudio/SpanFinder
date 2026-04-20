using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Thumbs;

/// <summary>
/// 워커 전용 로그. 메인 DebugLogger와 같은 폴더에 별도 prefix로 저장.
/// 위치: %LocalAppData%\Span\Logs\Span_Worker_yyyyMMdd_HHmmss_pid{pid}.log
///
/// M1: ConcurrentQueue + 백그라운드 flush — 호출 스레드 디스크 I/O 차단 제거.
/// 워커 종료 시 FlushSync로 누락 방지.
/// </summary>
internal static class WorkerLogger
{
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly ManualResetEventSlim _hasItems = new(false);
    private static readonly object _writeLock = new();
    private static readonly string LogPath;
    private static volatile bool _stopped;

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

        var t = new Thread(FlushLoop) { IsBackground = true, Name = "Span.Thumbs.Logger" };
        t.Start();
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.Error.WriteLine(line);  // 메인이 stderr 캡처 가능
        _queue.Enqueue(line);
        _hasItems.Set();
    }

    /// <summary>워커 종료 직전 호출 — 남은 큐 모두 동기 flush.</summary>
    public static void Stop()
    {
        _stopped = true;
        _hasItems.Set();
        FlushNow();
    }

    private static void FlushLoop()
    {
        while (!_stopped)
        {
            _hasItems.Wait();
            _hasItems.Reset();
            FlushNow();
        }
        // 종료 신호 후 마지막 잔여
        FlushNow();
    }

    private static void FlushNow()
    {
        if (_queue.IsEmpty) return;
        try
        {
            lock (_writeLock)
            {
                using var sw = new StreamWriter(LogPath, append: true);
                while (_queue.TryDequeue(out var line))
                {
                    sw.WriteLine(line);
                }
            }
        }
        catch { /* ignore — 디스크 가득/권한 문제 */ }
    }
}
