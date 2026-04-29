using System;
using System.Threading.Tasks;

namespace Span.Helpers
{
    /// <summary>
    /// v1.4.15: fire-and-forget Task에서 발생한 unobserved exception이
    /// finalizer thread의 TaskScheduler.UnobservedTaskException으로 재던져져 Sentry 노이즈를 만드는 문제 방지.
    ///
    /// 사용:
    ///   _ = SomeAsync();           // ❌ unobserved 위험
    ///   SomeAsync().FireAndForget(); // ✅ 자동 observe + 로그
    /// </summary>
    internal static class TaskExtensions
    {
        /// <summary>
        /// Task 실행을 의도적으로 await하지 않는 경우, 실패 시 자동 observe + 로그.
        /// OperationCanceledException은 정상 cancel 흐름이므로 로그만 남기고 swallow.
        /// </summary>
        /// <param name="task">await하지 않을 Task</param>
        /// <param name="caller">로그 식별용 호출자 태그 (선택)</param>
        public static void FireAndForget(this Task task, string? caller = null)
        {
            if (task == null) return;
            // 이미 완료/실패된 Task도 Exception 접근으로 observe 처리 필요
            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    var ex = task.Exception;
                    LogError(ex, caller);
                }
                return;
            }

            task.ContinueWith(
                t =>
                {
                    if (t.Exception != null)
                        LogError(t.Exception, caller);
                },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static void LogError(AggregateException? ex, string? caller)
        {
            if (ex == null) return;
            try
            {
                // OperationCanceled는 정상 종료 — 무시
                if (ex.InnerException is OperationCanceledException) return;
                var tag = string.IsNullOrEmpty(caller) ? "FireAndForget" : $"FireAndForget:{caller}";
                DebugLogger.Log($"[{tag}] {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            }
            catch { /* 로그 자체에서 throw해도 절대 새지 않게 */ }
        }
    }
}
