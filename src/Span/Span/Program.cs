using Microsoft.Windows.AppLifecycle;
using System;

namespace Span;

class Program
{
    // ---- 진단 로그 (App() ctor 이전 구간 추적용) --------------------
    // EarlyBootLog 는 App.xaml.cs 에 있지만 Program.Main 에서는 App 타입을
    // 참조하기 전이라 호출 시점이 module-init 을 트리거할 수 있음. 독립 구현.
    private static void BootLog(string msg)
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logsDir = System.IO.Path.Combine(baseDir, "Span", "Logs");
            System.IO.Directory.CreateDirectory(logsDir);
            var path = System.IO.Path.Combine(logsDir, "Span_Boot.log");
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [Program] {msg}\n";
            System.IO.File.AppendAllText(path, line);
        }
        catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        BootLog("=== Span.exe Main 진입 ===");
        try
        {
            BootLog("InitializeComWrappers 호출 전");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            BootLog("InitializeComWrappers OK");

            BootLog("DecideRedirection 호출 전");
            var isRedirect = DecideRedirection();
            BootLog($"DecideRedirection OK (isRedirect={isRedirect})");

            if (!isRedirect)
            {
                BootLog("Application.Start 호출 전");
                Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    BootLog("Application.Start 콜백 진입");
                    var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                    System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                    BootLog("SynchronizationContext 설정 OK, new App() 호출 전");
                    new App();
                    BootLog("new App() 반환 (ctor 완료)");
                });
                BootLog("Application.Start 반환 (메시지 루프 종료)");
            }
        }
        catch (Exception ex)
        {
            BootLog($"*** Main CRASH *** {ex.GetType().FullName}: {ex.Message}");
            BootLog($"    StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
                BootLog($"    Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            throw;
        }

        BootLog("Main 정상 종료 (return 0)");
        return 0;
    }

    private static bool DecideRedirection()
    {
        BootLog("  DecideRedirection → FindOrRegisterForKey 호출 전");
        var appInstance = AppInstance.FindOrRegisterForKey("SPAN_FINDER_MAIN");
        BootLog($"  DecideRedirection → FindOrRegisterForKey OK (IsCurrent={appInstance.IsCurrent})");

        if (appInstance.IsCurrent)
            return false; // 첫 인스턴스 — 정상 실행

        // 기존 인스턴스로 활성화 리다이렉트
        BootLog("  DecideRedirection → GetActivatedEventArgs 호출 전");
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        BootLog("  DecideRedirection → RedirectActivationToAsync 호출 전");
        appInstance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
        BootLog("  DecideRedirection → 리다이렉트 완료");
        return true; // 현재 프로세스 종료
    }
}
