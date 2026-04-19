using System;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Span.Helpers
{
    /// <summary>
    /// Debug logger that writes to Debug output synchronously
    /// and flushes to file asynchronously via Channel.
    /// 세션별 타임스탬프 파일명으로 이전 크래시 로그를 보존한다 (7일 / 50개 보관).
    /// </summary>
    public static class DebugLogger
    {
        private const string LogFilePrefix = "Span_Debug_";
        private const string LogFileSuffix = ".log";
        private const string LegacyLogFileName = "Span_Debug.log";
        private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
        private const int MaxRetainedFiles = 50;

        private static readonly string LogsDir;
        private static readonly string LogFilePath;

        private static string InitLogsDir()
        {
            // MSIX 패키지 앱은 AppContext.BaseDirectory(Program Files\WindowsApps)가 읽기 전용.
            // LocalApplicationData로 변경하여 Store 배포에서도 로그 파일 생성 보장.
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(baseDir, "Span", "Logs");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static readonly Channel<string> _channel =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        static DebugLogger()
        {
            LogsDir = InitLogsDir();

            // 세션별 파일명 — 이전 세션 크래시 로그 보존
            var sessionTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            LogFilePath = Path.Combine(LogsDir, $"{LogFilePrefix}{sessionTag}{LogFileSuffix}");

            // 시작 시 1회 정리 (7일 초과 + 50개 초과)
            try { CleanupOldLogs(); } catch { }

            try
            {
                File.WriteAllText(LogFilePath, $"=== Span Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
            }
            catch { }

            // Background consumer — writes batches to file
            Task.Run(ConsumeLogsAsync);
        }

        /// <summary>
        /// 보관 기간(7일) 초과 또는 개수(50개) 초과 로그 파일을 삭제.
        /// 레거시 파일명(Span_Debug.log)도 함께 정리.
        /// </summary>
        private static void CleanupOldLogs()
        {
            if (!Directory.Exists(LogsDir)) return;

            // 레거시 파일은 무조건 삭제 (한 번만 발생)
            var legacyPath = Path.Combine(LogsDir, LegacyLogFileName);
            try { if (File.Exists(legacyPath)) File.Delete(legacyPath); } catch { }

            var files = Directory.EnumerateFiles(LogsDir, $"{LogFilePrefix}*{LogFileSuffix}")
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            var threshold = DateTime.UtcNow - RetentionPeriod;
            for (int i = 0; i < files.Count; i++)
            {
                var fi = files[i];
                bool tooOld = fi.LastWriteTimeUtc < threshold;
                bool tooMany = i >= MaxRetainedFiles;
                if (tooOld || tooMany)
                {
                    try { fi.Delete(); } catch { /* ignore */ }
                }
            }
        }

        public static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}";

            System.Diagnostics.Debug.WriteLine(logMessage);

            // Non-blocking enqueue — never blocks UI thread
            _channel.Writer.TryWrite(logMessage);
        }

        public static string GetLogFilePath() => LogFilePath;

        /// <summary>
        /// Synchronous crash-time logging. Writes directly to file (bypasses async channel)
        /// because the process may die before the channel consumer flushes.
        /// </summary>
        public static void LogCrash(string context, Exception? ex)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var message = $"[{timestamp}] *** CRASH *** [{context}] {ex?.GetType().Name}: {ex?.Message}\n" +
                          $"  StackTrace: {ex?.StackTrace}\n" +
                          (ex?.InnerException != null
                              ? $"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n" +
                                $"  InnerTrace: {ex.InnerException.StackTrace}\n"
                              : "");

            System.Diagnostics.Debug.WriteLine(message);

            // Synchronous write — must succeed before process dies
            try
            {
                File.AppendAllText(LogFilePath, message + "\n");
            }
            catch { /* last resort — nothing we can do */ }
        }

        /// <summary>
        /// Flush pending log entries. Call on app shutdown.
        /// </summary>
        public static void Shutdown()
        {
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// 크래시 시점에 channel에 남아있는 로그를 동기로 파일에 flush.
        /// AttachLogFile 호출 전에 실행하여 최신 로그가 첨부되도록 보장.
        /// </summary>
        public static void FlushSync()
        {
            try
            {
                using var sw = new StreamWriter(LogFilePath, append: true);
                while (_channel.Reader.TryRead(out var msg))
                {
                    sw.WriteLine(msg);
                }
            }
            catch { }
        }

        private static async Task ConsumeLogsAsync()
        {
            var reader = _channel.Reader;
            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    using var sw = new StreamWriter(LogFilePath, append: true);
                    // Drain all available messages in a batch
                    while (reader.TryRead(out var msg))
                    {
                        sw.WriteLine(msg);
                    }
                }
            }
            catch
            {
                // Ignore file write errors
            }
        }
    }
}
