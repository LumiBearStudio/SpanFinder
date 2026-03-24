using Microsoft.Windows.AppLifecycle;
using System;

namespace Span;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var isRedirect = DecideRedirection();
        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }

        return 0;
    }

    private static bool DecideRedirection()
    {
        var appInstance = AppInstance.FindOrRegisterForKey("SPAN_FINDER_MAIN");

        if (appInstance.IsCurrent)
            return false; // 첫 인스턴스 — 정상 실행

        // 기존 인스턴스로 활성화 리다이렉트
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        appInstance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
        return true; // 현재 프로세스 종료
    }
}
