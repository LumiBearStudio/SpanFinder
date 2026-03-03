using System;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace Span.Helpers
{
    /// <summary>
    /// DispatcherQueue.TryEnqueue 래퍼.
    /// WinUI 3에서 TryEnqueue 내부 예외는 글로벌 UnhandledException 핸들러를 우회하여
    /// 앱이 즉시 크래시되므로, 모든 콜백을 try-catch로 보호한다.
    /// 예외 발생 시 DebugLogger에 기록하고 Sentry로 전송한다.
    /// </summary>
    public static class DispatcherHelper
    {
        /// <summary>
        /// DispatcherQueue에 안전하게 작업을 예약한다 (기본 우선순위).
        /// </summary>
        public static void SafeEnqueue(
            DispatcherQueue dq,
            Action action,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "")
        {
            dq.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    HandleException(ex, caller, file);
                }
            });
        }

        /// <summary>
        /// DispatcherQueue에 안전하게 작업을 예약한다 (우선순위 지정).
        /// </summary>
        public static void SafeEnqueue(
            DispatcherQueue dq,
            DispatcherQueuePriority priority,
            Action action,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "")
        {
            dq.TryEnqueue(priority, () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    HandleException(ex, caller, file);
                }
            });
        }

        private static void HandleException(Exception ex, string caller, string file)
        {
            var fileName = Path.GetFileName(file);
            DebugLogger.Log($"[SafeEnqueue] {fileName}:{caller} — {ex.Message}");
            try
            {
                App.Current.Services.GetService<Services.CrashReportingService>()
                    ?.CaptureException(ex, $"SafeEnqueue:{fileName}:{caller}");
            }
            catch
            {
                // Sentry 전송 실패해도 앱은 계속 실행
            }
        }
    }
}
