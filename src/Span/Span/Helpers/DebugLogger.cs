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
            // MSIX Packaged: ApplicationData.Current.LocalFolder.Path 가 실제 packaged LocalState 경로를 반환.
            //   예: C:\Users\{user}\AppData\Local\Packages\LumiBearStudio.SPANFinder_*\LocalState\Logs
            // Environment.SpecialFolder.LocalApplicationData는 virtualize되지 않은 논리 경로를 주므로
            //   탐색기로 열었을 때 "없는 폴더"로 보이는 문제 발생 → Packaged API 우선.
            string dir;
            try
            {
                var localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                dir = Path.Combine(localState, "Logs");
            }
            catch
            {
                // UnPackaged fallback (개발 실행 등)
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dir = Path.Combine(baseDir, "Span", "Logs");
            }
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

        public static string GetLogsDirectory() => LogsDir;

        /// <summary>
        /// 현재 세션을 제외한 가장 최근 로그 파일 경로 반환.
        /// 비정상 종료 감지(Phase 0)에서 사용 — 마지막 줄에 [Shutdown] clean exit 마커가 있는지 검사.
        /// </summary>
        public static string? GetPreviousSessionLogPath()
        {
            try
            {
                if (!Directory.Exists(LogsDir)) return null;
                var files = Directory.EnumerateFiles(LogsDir, $"{LogFilePrefix}*{LogFileSuffix}")
                    .Where(p => !string.Equals(p, LogFilePath, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();
                return files?.FullName;
            }
            catch
            {
                return null;
            }
        }

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
        /// 정상 종료 마커([Shutdown] clean exit)를 기록하여 다음 시작 시
        /// 비정상 종료 여부를 판별할 수 있게 한다 (Phase 0).
        ///
        /// 핵심: ConsumeLogsAsync가 StreamWriter(append: true, FileShare.Read)로 파일을
        /// 잠시 잡고 있는 순간 File.AppendAllText(기본 FileShare.Read)가 IOException으로
        /// 실패 → catch에 삼켜져 마커 누락 → 다음 실행 post-mortem 오탐. 이를 피하려고
        /// FileShare.ReadWrite + 짧은 retry로 경합에 강하게 작성한다.
        /// </summary>
        public static void Shutdown()
        {
            // 먼저 채널을 닫아 consumer가 남은 메시지를 drain 후 종료하도록 유도
            try { _channel.Writer.TryComplete(); } catch { }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var marker = $"[{timestamp}] [Shutdown] clean exit\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(marker);

            // FileShare.ReadWrite로 오픈해 consumer StreamWriter와 공존 가능.
            // 드물게 OS 레벨 경합이 있어도 10ms 간격 최대 10회 재시도 (약 100ms 이내 성공).
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var fs = new FileStream(
                        LogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);  // OS 버퍼까지 flush — Process.Kill 전 확정
                    return;
                }
                catch (IOException)
                {
                    try { System.Threading.Thread.Sleep(10); } catch { }
                }
                catch
                {
                    // 다른 예외(ACL/디스크 등)는 종료 직전이므로 포기
                    return;
                }
            }
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
