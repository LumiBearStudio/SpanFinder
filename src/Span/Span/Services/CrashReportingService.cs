using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sentry;

namespace Span.Services;

/// <summary>
/// Sentry 기반 크래시 리포팅 서비스.
/// - 지연 초기화: 백그라운드 스레드에서 SDK 초기화 (앱 시작 차단 없음)
/// - 경로 스크러빙: 사용자 폴더 경로를 해시로 치환
/// - 디버깅 필수 정보: OS, .NET, 앱 버전, 스택 트레이스, 메모리, 뷰 상태
/// - 설정 ON/OFF: EnableCrashReporting 토글로 제어
/// </summary>
public sealed class CrashReportingService : IDisposable
{
    // Sentry DSN: 환경변수 SPAN_SENTRY_DSN 또는 앱 설정에서 로드
    // 빌드 시 CI/CD에서 주입하거나 로컬 개발 시 환경변수로 설정
    private static readonly string? SentryDsn =
        Environment.GetEnvironmentVariable("SPAN_SENTRY_DSN")
        ?? GetDsnFromAppSettings();

    private volatile IDisposable? _sentryDisposable;
    private volatile bool _isEnabled;
    private readonly SettingsService _settings;
    private int _initializing; // 0 = idle, 1 = in progress (interlocked guard)

    // 경로 스크러빙용 패턴 — Lazy로 JIT 컴파일 비용을 첫 크래시 시점까지 지연
    private static readonly Lazy<Regex> UserPathRegex = new(
        () => new Regex(@"(?i)([A-Z]:\\Users\\)[^\\""]+", RegexOptions.Compiled));

    public CrashReportingService(SettingsService settings)
    {
        _settings = settings;
        _isEnabled = settings.EnableCrashReporting;

        settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>
    /// Sentry SDK 비동기 초기화. App 생성자에서 호출.
    /// 백그라운드에서 SDK를 초기화하므로 앱 시작을 차단하지 않는다.
    /// 초기화 완료 전 발생하는 예외는 캡처 누락될 수 있으나,
    /// 앱 시작 속도가 우선이므로 허용 가능한 트레이드오프.
    /// </summary>
    public void Initialize()
    {
        if (!_isEnabled) return;
        if (string.IsNullOrEmpty(SentryDsn))
        {
            Helpers.DebugLogger.Log("[CrashReporting] Skipped — DSN not configured (set SPAN_SENTRY_DSN env var)");
            return;
        }

#if DEBUG
        // DEBUG 빌드에서는 Sentry 전송 불필요 — 로컬 디버거 사용
        Helpers.DebugLogger.Log("[CrashReporting] Skipped in DEBUG build");
        return;
#else
        Task.Run(() => StartSentry());
#endif
    }

    /// <summary>
    /// 예외 캡처 + 경로 스크러빙.
    /// </summary>
    public void CaptureException(Exception ex, string context, Dictionary<string, string>? extras = null)
    {
        if (!_isEnabled || _sentryDisposable == null) return;

        try
        {
            var sentryId = SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("crash.context", context);
                scope.SetExtra("memory.workingSet", Environment.WorkingSet / 1024 / 1024 + " MB");
                scope.SetExtra("memory.gcTotal", GC.GetTotalMemory(false) / 1024 / 1024 + " MB");

                if (extras != null)
                {
                    foreach (var kv in extras)
                        scope.SetExtra(kv.Key, ScrubPaths(kv.Value));
                }
            });
        }
        catch
        {
            // Sentry 전송 실패가 앱에 영향을 주면 안 됨
        }
    }

    /// <summary>
    /// UI 컨텍스트 정보를 Breadcrumb으로 기록 (크래시 전 유저 행동 추적).
    /// </summary>
    public void AddBreadcrumb(string message, string category = "ui")
    {
        if (!_isEnabled || _sentryDisposable == null) return;
        try
        {
            SentrySdk.AddBreadcrumb(ScrubPaths(message), category);
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] AddBreadcrumb failed: {ex.Message}"); }
    }

    /// <summary>
    /// 앱 상태 태그 업데이트 (뷰 모드, 탭 수 등).
    /// </summary>
    public void SetAppContext(string viewMode, int tabCount, bool isSplitView)
    {
        if (!_isEnabled || _sentryDisposable == null) return;
        try
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.SetTag("app.viewMode", viewMode);
                scope.SetTag("app.tabCount", tabCount.ToString());
                scope.SetTag("app.splitView", isSplitView.ToString());
            });
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] SetAppContext failed: {ex.Message}"); }
    }

    private void StartSentry()
    {
        if (_sentryDisposable != null) return;
        if (string.IsNullOrEmpty(SentryDsn)) return;

        // 동시 초기화 방지 (Task.Run + OnSettingChanged 경합)
        if (Interlocked.CompareExchange(ref _initializing, 1, 0) != 0) return;

        try
        {
            _sentryDisposable = SentrySdk.Init(options =>
            {
                options.Dsn = SentryDsn;
                options.Release = $"span@{GetAppVersion()}";
                options.Environment = "production";

                // 오프라인 캐시: 크래시 전송 실패 시 로컬 저장 → 다음 실행 시 재전송
                options.CacheDirectoryPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Span", "SentryCache");

                // 세션 추적 비활성화 — 파일 탐색기에 불필요한 HTTP 요청 제거
                options.AutoSessionTracking = false;
                options.IsGlobalModeEnabled = true;

                // ── 디버깅에 필요한 정보 수집 ──
                options.AttachStacktrace = true;
                options.SendDefaultPii = false;  // 이메일/IP 등 개인정보 비수집
                options.MaxBreadcrumbs = 50;

                // ── 경로 스크러빙 (BeforeSend) ──
                options.SetBeforeSend((sentryEvent, hint) =>
                {
                    return ScrubEvent(sentryEvent);
                });
            });

            Helpers.DebugLogger.Log("[CrashReporting] Sentry initialized (background)");
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[CrashReporting] Sentry init failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _initializing, 0);
        }
    }

    private void StopSentry()
    {
        try
        {
            _sentryDisposable?.Dispose();
            _sentryDisposable = null;
            Helpers.DebugLogger.Log("[CrashReporting] Sentry stopped");
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] StopSentry failed: {ex.Message}"); }
    }

    private void OnSettingChanged(string key, object? value)
    {
        if (key != "EnableCrashReporting" || value is not bool enabled) return;

        _isEnabled = enabled;
        if (enabled)
            StartSentry();
        else
            StopSentry();
    }

    // ══════════════════════════════════════════════════════════════
    //  경로 스크러빙 (Path Scrubbing)
    //  목적: 디버깅 정보는 유지하되 사용자 개인 폴더명 제거
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// SentryEvent 전체를 스크러빙.
    /// 유지: 스택 트레이스, 예외 타입, OS/앱 버전
    /// 제거: 사용자명, 머신명, 파일 경로의 사용자 폴더명
    /// </summary>
    private static SentryEvent ScrubEvent(SentryEvent evt)
    {
        // 서버명/유저명 제거
        evt.ServerName = null;
        evt.User = null;

        // Exception 메시지에 경로가 포함될 수 있으므로 스크러빙된 버전을 Extra에 추가
        if (evt.Exception != null)
        {
            evt.SetExtra("scrubbed.message", ScrubPaths(evt.Exception.Message));
        }

        // Extra 데이터 스크러빙
        if (evt.Extra != null)
        {
            var scrubbedExtras = new Dictionary<string, object?>();
            foreach (var kv in evt.Extra)
            {
                scrubbedExtras[kv.Key] = kv.Value is string s ? ScrubPaths(s) : kv.Value;
            }
            foreach (var kv in scrubbedExtras)
                evt.SetExtra(kv.Key, kv.Value);
        }

        return evt;
    }

    /// <summary>
    /// 문자열에서 사용자 경로를 스크러빙.
    /// "C:\Users\김철수\Documents\비밀\file.txt"
    ///   → "C:\Users\***\Documents\비밀\file.txt"
    /// 드라이브 레터, 파일명, 확장자는 유지 (디버깅에 필요).
    /// </summary>
    internal static string ScrubPaths(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // C:\Users\{username}\ → C:\Users\***\
        return UserPathRegex.Value.Replace(input, "$1***");
    }

    /// <summary>
    /// 앱 설정(LocalSettings)에서 Sentry DSN을 로드.
    /// CI/CD 빌드에서 MSIX 패키지 설정으로 주입 가능.
    /// </summary>
    private static string? GetDsnFromAppSettings()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            return settings.Values.TryGetValue("SentryDsn", out var dsn) ? dsn as string : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        StopSentry();
    }
}
