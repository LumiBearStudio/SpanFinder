using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Span.Helpers
{
    /// <summary>
    /// Debug logger that writes to Debug output synchronously
    /// and flushes to file asynchronously via Channel.
    /// </summary>
    public static class DebugLogger
    {
        private static readonly string LogFilePath;

        private static string InitLogFilePath()
        {
            var exeDir = AppContext.BaseDirectory;
            var logsDir = Path.Combine(exeDir, "Logs");
            try { Directory.CreateDirectory(logsDir); } catch { }
            return Path.Combine(logsDir, "Span_Debug.log");
        }

        private static readonly Channel<string> _channel =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        static DebugLogger()
        {
            LogFilePath = InitLogFilePath();
            try
            {
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }
                File.WriteAllText(LogFilePath, $"=== Span Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
            }
            catch { }

            // Background consumer — writes batches to file
            Task.Run(ConsumeLogsAsync);
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
