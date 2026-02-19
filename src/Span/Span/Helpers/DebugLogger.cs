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
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "Span_Debug.log"
        );

        private static readonly Channel<string> _channel =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        static DebugLogger()
        {
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
