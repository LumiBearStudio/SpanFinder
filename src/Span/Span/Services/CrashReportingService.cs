using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
    // Sentry DSN: 환경변수 > 앱 설정 > 기본값 순으로 로드
    // Note: Sentry DSN은 이벤트 전송 전용 (읽기 불가)이므로 클라이언트 앱에 포함해도 안전
    private const string DefaultDsn = "https://a7e1e9d16763c38024a495176e723b2a@o4510949994266624.ingest.de.sentry.io/4510950010191952";
    private static readonly string? SentryDsn =
        Environment.GetEnvironmentVariable("SPAN_SENTRY_DSN")
        ?? GetDsnFromAppSettings()
        ?? DefaultDsn;

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
    /// Sentry SDK 동기 초기화. App 생성자에서 호출.
    /// 글로벌 예외 핸들러 등록 전에 SDK가 준비되어야 초기 크래시를 캡처할 수 있으므로
    /// 동기로 초기화한다. SentrySdk.Init는 약 10-50ms로 충분히 빠르다.
    /// </summary>
    public void Initialize()
    {
        if (!_isEnabled)
        {
            Helpers.DebugLogger.Log("[CrashReporting] Disabled by user setting");
            return;
        }
        if (string.IsNullOrEmpty(SentryDsn))
        {
            Helpers.DebugLogger.Log("[CrashReporting] Skipped — DSN not configured (set SPAN_SENTRY_DSN env var)");
            return;
        }

        try
        {
            StartSentry();
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[Sentry] Init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 예외 캡처 + 경로 스크러빙 + 로그 파일 첨부.
    /// </summary>
    public void CaptureException(Exception ex, string context, Dictionary<string, string>? extras = null)
    {
        if (!_isEnabled || _sentryDisposable == null) return;

        try
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("crash.context", context);
                scope.SetExtra("memory.workingSet", Environment.WorkingSet / 1024 / 1024 + " MB");
                scope.SetExtra("memory.gcTotal", GC.GetTotalMemory(false) / 1024 / 1024 + " MB");

                if (extras != null)
                {
                    foreach (var kv in extras)
                        scope.SetExtra(kv.Key, ScrubPaths(kv.Value));
                }

                AttachLogFile(scope);
            });
        }
        catch
        {
            // Sentry 전송 실패가 앱에 영향을 주면 안 됨
        }
    }

    /// <summary>
    /// Fatal 크래시 캡처 — 로그 파일 첨부 + Flush로 프로세스 종료 전 전송 보장.
    /// DI 실패에 대비해 SentrySdk 직접 호출도 포함.
    /// </summary>
    public static void CaptureFatalException(Exception ex, string context)
    {
        try
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.Level = SentryLevel.Fatal;
                scope.SetTag("crash.context", context);
                scope.SetExtra("memory.workingSet", Environment.WorkingSet / 1024 / 1024 + " MB");
                scope.SetExtra("memory.gcTotal", GC.GetTotalMemory(false) / 1024 / 1024 + " MB");

                AttachLogFile(scope);
            });
            // 프로세스 종료 전 이벤트가 전송되도록 동기 대기 (최대 3초)
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch
        {
            // 프로세스 종료 직전 — 실패해도 무시
        }
    }

    /// <summary>
    /// 시작 직후 1회 호출 — 이전 세션 비정상 종료 감지 + WER 미니덤프 업로드 (Phase 0).
    /// 백그라운드 스레드에서 실행되도록 Task.Run으로 호출 권장.
    ///
    /// 동작:
    /// 1. 이전 세션 로그 파일 마지막 줄에 [Shutdown] clean exit 마커 검사
    /// 2. 마커 없음 → 비정상 종료로 판단 → post-mortem Sentry 이벤트 + 이전 로그 첨부
    /// 3. WER 덤프 폴더(%LocalAppData%\Span\CrashDumps) 스캔 → 각 .dmp를 Sentry attachment로 업로드
    /// 4. 업로드 성공 시 .dmp 삭제 (디스크 보호)
    /// </summary>
    public void DetectPreviousAbnormalExitAndUploadDumps()
    {
        if (!_isEnabled || _sentryDisposable == null) return;

#if DEBUG
        // 디버그 빌드: 디버거 stop / 빌드 재시작이 false-positive로 잡히는 노이즈 방지.
        // 실사용자(release)만 post-mortem 보고 → Sentry 시그널 정확도 유지.
        Helpers.DebugLogger.Log("[CrashReporting] post-mortem skipped (DEBUG build)");
        return;
#else
        try
        {
            DetectPreviousAbnormalExit();
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] DetectPreviousAbnormalExit failed: {ex.Message}"); }

        try
        {
            UploadAndCleanWerDumps();
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] UploadAndCleanWerDumps failed: {ex.Message}"); }
#endif
    }

    private void DetectPreviousAbnormalExit()
    {
        var prevLogPath = Helpers.DebugLogger.GetPreviousSessionLogPath();
        if (string.IsNullOrEmpty(prevLogPath) || !File.Exists(prevLogPath))
        {
            Helpers.DebugLogger.Log("[CrashReporting] No previous session log found");
            return;
        }

        // 마지막 5줄 검사 — [Shutdown] clean exit 마커 존재 여부
        bool cleanExit = false;
        try
        {
            var lastLines = ReadLastLines(prevLogPath, 5);
            cleanExit = lastLines.Any(l => l.Contains("[Shutdown] clean exit", StringComparison.Ordinal));
        }
        catch { /* 읽기 실패 시 비정상으로 간주 안 함 — 노이즈 방지 */ return; }

        if (cleanExit)
        {
            Helpers.DebugLogger.Log($"[CrashReporting] Previous session ended cleanly: {Path.GetFileName(prevLogPath)}");
            return;
        }

        Helpers.DebugLogger.Log($"[CrashReporting] Previous session abnormal exit detected: {Path.GetFileName(prevLogPath)}");

        // post-mortem 이벤트 — 일반 메시지로 보내고 이전 로그 첨부
        try
        {
            SentrySdk.CaptureMessage(
                "post-mortem: previous session ended without clean exit marker",
                scope =>
                {
                    scope.Level = SentryLevel.Error;
                    scope.SetTag("crash.context", "post-mortem.abnormal-exit");
                    scope.SetExtra("prev.log.fileName", Path.GetFileName(prevLogPath));
                    AttachSpecificLogFile(scope, prevLogPath, "Span_PreviousSession.log");
                });
        }
        catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] post-mortem capture failed: {ex.Message}"); }
    }

    private void UploadAndCleanWerDumps()
    {
        var dumps = Helpers.WerHelper.EnumerateDumps();
        if (dumps.Length == 0) return;

        Helpers.DebugLogger.Log($"[CrashReporting] Found {dumps.Length} WER dump(s)");

        const long MaxDumpAttachmentBytes = 10 * 1024 * 1024; // 10MB Sentry 제한 고려

        foreach (var dumpPath in dumps)
        {
            try
            {
                var fi = new FileInfo(dumpPath);
                if (!fi.Exists) continue;

                // 너무 큰 덤프는 메타만 보고 + 파일 삭제 (디스크 보호)
                if (fi.Length > MaxDumpAttachmentBytes)
                {
                    SentrySdk.CaptureMessage(
                        "WER minidump too large to attach",
                        scope =>
                        {
                            scope.Level = SentryLevel.Warning;
                            scope.SetTag("crash.context", "wer.dump.oversized");
                            scope.SetExtra("dump.fileName", fi.Name);
                            scope.SetExtra("dump.sizeBytes", fi.Length);
                        });
                    try { fi.Delete(); } catch { }
                    continue;
                }

                var bytes = File.ReadAllBytes(dumpPath);
                SentrySdk.CaptureMessage(
                    "WER minidump captured (post-mortem)",
                    scope =>
                    {
                        scope.Level = SentryLevel.Fatal;
                        scope.SetTag("crash.context", "wer.dump");
                        scope.SetExtra("dump.fileName", fi.Name);
                        scope.SetExtra("dump.sizeBytes", fi.Length);
                        scope.SetExtra("dump.lastWriteUtc", fi.LastWriteTimeUtc.ToString("o"));
                        scope.AddAttachment(bytes, fi.Name, AttachmentType.Minidump, "application/octet-stream");
                    });

                // 동기 flush 후 삭제 (전송 보장)
                try { SentrySdk.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); } catch { }
                try { fi.Delete(); } catch (Exception ex) { Helpers.DebugLogger.Log($"[CrashReporting] dump delete failed: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[CrashReporting] dump upload failed for {Path.GetFileName(dumpPath)}: {ex.Message}");
            }
        }
    }

    private static string[] ReadLastLines(string path, int n)
    {
        // 작은 로그 파일이면 전체 읽기, 큰 파일이면 tail
        var fi = new FileInfo(path);
        if (fi.Length < 64 * 1024)
        {
            try { return File.ReadAllLines(path).Reverse().Take(n).Reverse().ToArray(); }
            catch { return Array.Empty<string>(); }
        }

        // 마지막 16KB만 읽어서 줄 단위 파싱
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var readSize = (int)Math.Min(fs.Length, 16 * 1024);
            fs.Seek(-readSize, SeekOrigin.End);
            var buf = new byte[readSize];
            fs.Read(buf, 0, readSize);
            var text = Encoding.UTF8.GetString(buf);
            var lines = text.Split('\n');
            return lines.Reverse().Take(n).Reverse().Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void AttachSpecificLogFile(Scope scope, string logPath, string attachName)
    {
        try
        {
            var bytes = ReadTailBytes(logPath, MaxLogAttachmentBytes);
            if (bytes == null || bytes.Length == 0)
            {
                scope.SetExtra("log.attach.status", "empty");
                return;
            }
            var scrubbed = Encoding.UTF8.GetBytes(ScrubPaths(Encoding.UTF8.GetString(bytes)));
            scope.AddAttachment(scrubbed, attachName, AttachmentType.Default, "text/plain");
        }
        catch (Exception ex)
        {
            scope.SetExtra("log.attach.status", $"error: {ex.Message}");
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
#if DEBUG
                options.Environment = "debug";
#else
                options.Environment = "production";
#endif

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

    // ══════════════════════════════════════════════════════════════
    //  로그 파일 첨부 (Log File Attachment)
    //  목적: Sentry 이벤트에 최근 로그를 첨부하여 크래시 전후 컨텍스트 제공
    //  - 마지막 128KB만 전송 (Sentry 첨부파일 제한 고려)
    //  - 경로 스크러빙 적용하여 사용자 폴더명 제거
    // ══════════════════════════════════════════════════════════════

    private const int MaxLogAttachmentBytes = 128 * 1024; // 128KB

    /// <summary>
    /// Sentry scope에 로그 파일을 첨부. 경로 스크러빙 후 마지막 128KB만 전송.
    /// </summary>
    private static void AttachLogFile(Scope scope)
    {
        try
        {
            // Channel에 대기 중인 로그를 파일로 flush (크래시 직전 로그 누락 방지)
            Helpers.DebugLogger.FlushSync();

            var logPath = Helpers.DebugLogger.GetLogFilePath();
            if (string.IsNullOrEmpty(logPath))
            {
                scope.SetExtra("log.status", "path_empty");
                return;
            }
            if (!File.Exists(logPath))
            {
                scope.SetExtra("log.status", $"file_not_found: {ScrubPaths(logPath)}");
                return;
            }

            var logBytes = ReadTailBytes(logPath, MaxLogAttachmentBytes);
            if (logBytes == null || logBytes.Length == 0)
            {
                scope.SetExtra("log.status", "file_empty");
                return;
            }

            // 경로 스크러빙 적용
            var logText = Encoding.UTF8.GetString(logBytes);
            var scrubbedText = ScrubPaths(logText);
            var scrubbedBytes = Encoding.UTF8.GetBytes(scrubbedText);

            var attachName = Path.GetFileName(logPath);
            if (string.IsNullOrEmpty(attachName)) attachName = "Span_Debug.log";
            scope.AddAttachment(scrubbedBytes, attachName, AttachmentType.Default, "text/plain");
            scope.SetExtra("log.status", $"attached ({scrubbedBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            scope.SetExtra("log.status", $"error: {ex.Message}");
        }
    }

    /// <summary>
    /// 파일의 마지막 N바이트를 읽는다. 파일이 잠겨있어도 읽을 수 있도록 FileShare.ReadWrite 사용.
    /// </summary>
    private static byte[]? ReadTailBytes(string path, int maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = fs.Length;
            if (length == 0) return null;

            var readSize = (int)Math.Min(length, maxBytes);
            var buffer = new byte[readSize];

            if (length > maxBytes)
                fs.Seek(-maxBytes, SeekOrigin.End);

            var bytesRead = 0;
            while (bytesRead < readSize)
            {
                var read = fs.Read(buffer, bytesRead, readSize - bytesRead);
                if (read == 0) break;
                bytesRead += read;
            }

            return buffer;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        StopSentry();
    }
}
