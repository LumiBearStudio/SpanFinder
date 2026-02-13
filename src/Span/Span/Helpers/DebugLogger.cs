using System;
using System.IO;

namespace Span.Helpers
{
    /// <summary>
    /// Debug logger that writes to both Debug output and a file
    /// </summary>
    public static class DebugLogger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "Span_Debug.log"
        );

        private static readonly object _lock = new object();

        static DebugLogger()
        {
            // Clear log file on app start
            try
            {
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }

                // Write header
                File.WriteAllText(LogFilePath, $"=== Span Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
            }
            catch { }
        }

        public static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}";

            // Write to Debug output
            System.Diagnostics.Debug.WriteLine(logMessage);

            // Write to file
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, logMessage + "\n");
                }
                catch
                {
                    // Ignore file write errors
                }
            }
        }

        public static string GetLogFilePath() => LogFilePath;
    }
}
