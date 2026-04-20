using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Thumbs;

/// <summary>
/// Span Thumbnail Worker entry point.
///
/// 사용법: Span.Thumbs.exe --pipe &lt;pipeName&gt; [--cache &lt;cacheDir&gt;]
///   --pipe   메인이 지정한 NamedPipe 이름 (필수)
///   --cache  PNG 출력 폴더 (기본: %LocalAppData%\Span\ThumbCache)
///
/// 동작:
///   1. NamedPipeServerStream 생성 → 메인 연결 대기
///   2. JSON Lines 라인별 읽어서 처리
///      - gen → ThumbnailGenerator → PNG 파일 저장 → ok 응답
///      - cancel / cancel-batch → 진행 중 작업 취소
///      - ping → pong (메모리/완료 카운터)
///      - shutdown → 종료
///   3. Pipe broken → 종료 (메인 다음 spawn 시 새로 시작)
///
/// 외부 라이브러리 0 (System.IO.Pipes / System.Text.Json).
/// </summary>
internal static class Program
{
    private static readonly ThumbnailGenerator _generator = new();
    private static readonly ConcurrentDictionary<long, CancellationTokenSource> _inflight = new();
    private static long _completedCount;
    private static long _maxCancelledId;
    private static string _cacheDir = "";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            // ── 1. 인자 파싱 ──
            string? pipeName = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pipe" && i + 1 < args.Length) { pipeName = args[++i]; }
                else if (args[i] == "--cache" && i + 1 < args.Length) { _cacheDir = args[++i]; }
            }

            if (string.IsNullOrEmpty(pipeName))
            {
                WorkerLogger.Log("[Worker] FATAL: --pipe argument required");
                return 2;
            }

            if (string.IsNullOrEmpty(_cacheDir))
            {
                _cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Span", "ThumbCache");
            }
            try { Directory.CreateDirectory(_cacheDir); } catch { }

            WorkerLogger.Log($"[Worker] Started — pipe={pipeName} cache={_cacheDir} pid={Environment.ProcessId}");

            // ── 2. Sentry 초기화 (메인과 동일 DSN, tag로 구분) ──
            // Note: 메인의 SentryDsn 상수와 동일 — 환경변수로도 override 가능
            try
            {
                var dsn = Environment.GetEnvironmentVariable("SPAN_SENTRY_DSN")
                    ?? "https://a7e1e9d16763c38024a495176e723b2a@o4510949994266624.ingest.de.sentry.io/4510950010191952";
                Sentry.SentrySdk.Init(o =>
                {
                    o.Dsn = dsn;
                    o.AutoSessionTracking = false;
                    o.IsGlobalModeEnabled = true;
                    o.AttachStacktrace = true;
                    o.SendDefaultPii = false;
                    o.MaxBreadcrumbs = 50;
                    o.SetBeforeSend((evt, _) =>
                    {
                        evt.SetTag("process", "worker");
                        evt.ServerName = null;
                        evt.User = null;
                        return evt;
                    });
                });
                WorkerLogger.Log("[Worker] Sentry initialized");
            }
            catch (Exception ex) { WorkerLogger.Log($"[Worker] Sentry init failed: {ex.Message}"); }

            // ── 3. NamedPipe 서버 + 이벤트 루프 ──
            using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            WorkerLogger.Log("[Worker] Waiting for client connection...");
            // 30초 내 메인 연결 없으면 종료
            using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try { await pipe.WaitForConnectionAsync(connectCts.Token); }
                catch (OperationCanceledException)
                {
                    WorkerLogger.Log("[Worker] Connection timeout — exiting");
                    return 3;
                }
            }
            WorkerLogger.Log("[Worker] Client connected");

            // 라인 단위 reader/writer
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            // FlushAsync 보장 위해 AutoFlush
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

            // ── 4. 메인 루프 ──
            while (true)
            {
                string? line;
                try { line = await reader.ReadLineAsync(); }
                catch (Exception ex)
                {
                    WorkerLogger.Log($"[Worker] read failed: {ex.Message}");
                    break;
                }

                if (line == null)
                {
                    WorkerLogger.Log("[Worker] Pipe closed by client (EOF)");
                    break;
                }
                if (line.Length == 0) continue;

                IpcEnvelope? msg = null;
                try { msg = JsonSerializer.Deserialize<IpcEnvelope>(line, IpcJson.Options); }
                catch (Exception ex)
                {
                    WorkerLogger.Log($"[Worker] parse failed: {ex.Message} (line.len={line.Length})");
                    continue;
                }
                if (msg == null) continue;

                // 동기 처리 항목
                if (msg.Type == IpcMessageTypes.Shutdown)
                {
                    WorkerLogger.Log("[Worker] Shutdown requested");
                    break;
                }
                if (msg.Type == IpcMessageTypes.Ping)
                {
                    var pong = new IpcEnvelope
                    {
                        Type = IpcMessageTypes.Pong,
                        MemMB = Environment.WorkingSet / 1024 / 1024,
                        Completed = Interlocked.Read(ref _completedCount),
                    };
                    await SendAsync(writer, pong);
                    continue;
                }
                if (msg.Type == IpcMessageTypes.Cancel)
                {
                    if (_inflight.TryRemove(msg.Id, out var cts))
                    {
                        try { cts.Cancel(); } catch { }
                        cts.Dispose();
                    }
                    continue;
                }
                if (msg.Type == IpcMessageTypes.CancelBatch)
                {
                    // C4: CAS 루프로 lost update 방지
                    long cur, target;
                    do
                    {
                        cur = Interlocked.Read(ref _maxCancelledId);
                        target = Math.Max(cur, msg.MaxId);
                    }
                    while (Interlocked.CompareExchange(ref _maxCancelledId, target, cur) != cur);

                    var keys = _inflight.Keys.Where(k => k >= msg.MinId && k <= msg.MaxId).ToList();
                    foreach (var k in keys)
                    {
                        if (_inflight.TryRemove(k, out var cts))
                        {
                            try { cts.Cancel(); } catch { }
                            cts.Dispose();
                        }
                    }
                    continue;
                }

                // 비동기 처리 — gen 요청
                if (msg.Type == IpcMessageTypes.Gen)
                {
                    // C4 1차 가드: 이미 cancel-batch로 무효화된 ID
                    if (msg.Id <= Interlocked.Read(ref _maxCancelledId))
                    {
                        await SendErrAsync(writer, msg.Id, "cancelled-pre-start", retryable: false);
                        continue;
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));  // 작업당 타임아웃
                    _inflight[msg.Id] = cts;

                    // C4 2차 가드: 등록 직후 재확인 — register 직전에 cancel-batch가 도착했을 가능성
                    if (msg.Id <= Interlocked.Read(ref _maxCancelledId))
                    {
                        if (_inflight.TryRemove(msg.Id, out var c)) { try { c.Cancel(); c.Dispose(); } catch { } }
                        await SendErrAsync(writer, msg.Id, "cancelled-post-register", retryable: false);
                        continue;
                    }

                    var captured = msg;
                    _ = Task.Run(async () =>
                    {
                        try { await HandleGenAsync(writer, captured, cts.Token); }
                        catch (Exception ex)
                        {
                            try { await SendErrAsync(writer, captured.Id, "internal:" + ex.GetType().Name, retryable: false); }
                            catch { }
                            WorkerLogger.Log($"[Worker] gen #{captured.Id} crashed: {ex.Message}");
                        }
                        finally
                        {
                            if (_inflight.TryRemove(captured.Id, out var c)) { try { c.Dispose(); } catch { } }
                            Interlocked.Increment(ref _completedCount);
                        }
                    });
                    continue;
                }

                WorkerLogger.Log($"[Worker] Unknown type: {msg.Type}");
            }

            // I11: shutdown 시 _inflight CTS 정리
            foreach (var key in _inflight.Keys)
            {
                if (_inflight.TryRemove(key, out var c))
                {
                    try { c.Cancel(); } catch { }
                    try { c.Dispose(); } catch { }
                }
            }

            try { writer.Dispose(); } catch { }
            try { Sentry.SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
            WorkerLogger.Log("[Worker] Exiting cleanly");
            return 0;
        }
        catch (Exception ex)
        {
            try { WorkerLogger.Log($"[Worker] FATAL: {ex}"); } catch { }
            try { Sentry.SentrySdk.CaptureException(ex); Sentry.SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
            return 1;
        }
    }

    private static async Task HandleGenAsync(StreamWriter writer, IpcEnvelope req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Path))
        {
            await SendErrAsync(writer, req.Id, "missing-path", retryable: false);
            return;
        }

        // I4+A1: cachePath는 메인이 결정해서 IPC로 보냄 — 워커는 그대로 사용.
        // 양쪽 키 알고리즘 미러 제거 → 파일 mtime time-of-check-vs-use race 차단.
        if (string.IsNullOrEmpty(req.CachePath))
        {
            await SendErrAsync(writer, req.Id, "missing-cache-path", retryable: false);
            return;
        }

        // M5: path traversal 가드 — cachePath가 _cacheDir 하위인지 검증
        var fullCachePath = Path.GetFullPath(req.CachePath);
        var fullCacheDir = Path.GetFullPath(_cacheDir);
        if (!fullCachePath.StartsWith(fullCacheDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            await SendErrAsync(writer, req.Id, "cache-path-out-of-root", retryable: false);
            return;
        }

        try
        {
            var result = await _generator.GenerateAsync(
                req.Path, req.Size, req.Mode ?? "SingleItem",
                req.IsCloudOnly, req.ApplyExif, ct);

            if (result == null)
            {
                await SendErrAsync(writer, req.Id, "no-thumbnail", retryable: false);
                return;
            }

            try { Directory.CreateDirectory(Path.GetDirectoryName(fullCachePath)!); } catch { }

            // 원자적 쓰기: 임시 파일 → rename (PID 포함으로 다른 워커와 충돌 방지)
            var tmpPath = fullCachePath + $".tmp.{Environment.ProcessId}";
            await File.WriteAllBytesAsync(tmpPath, result.PngBytes, ct);
            try { File.Move(tmpPath, fullCachePath, overwrite: true); }
            catch (Exception ex)
            {
                WorkerLogger.Log($"[Worker] move failed for #{req.Id}: {ex.Message}");
                try { File.Delete(tmpPath); } catch { }
                await SendErrAsync(writer, req.Id, "move-failed", retryable: true);
                return;
            }

            var ok = new IpcEnvelope
            {
                Type = IpcMessageTypes.Ok,
                Id = req.Id,
                CachePath = fullCachePath,
                Width = result.Width,
                Height = result.Height,
                AppliedExif = result.AppliedExif,
            };
            await SendAsync(writer, ok);
        }
        catch (OperationCanceledException)
        {
            await SendErrAsync(writer, req.Id, "cancelled", retryable: false);
        }
    }

    private static async Task SendAsync(StreamWriter writer, IpcEnvelope msg)
    {
        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        try
        {
            await writer.WriteLineAsync(json);
        }
        catch (Exception ex) { WorkerLogger.Log($"[Worker] send failed: {ex.Message}"); }
    }

    private static Task SendErrAsync(StreamWriter writer, long id, string error, bool retryable)
        => SendAsync(writer, new IpcEnvelope
        {
            Type = IpcMessageTypes.Err,
            Id = id,
            Error = error,
            Retryable = retryable,
        });
}
