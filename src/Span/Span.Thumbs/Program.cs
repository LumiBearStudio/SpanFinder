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
                    var keys = _inflight.Keys.Where(k => k >= msg.MinId && k <= msg.MaxId).ToList();
                    foreach (var k in keys)
                    {
                        if (_inflight.TryRemove(k, out var cts))
                        {
                            try { cts.Cancel(); } catch { }
                            cts.Dispose();
                        }
                    }
                    Interlocked.Exchange(ref _maxCancelledId, Math.Max(_maxCancelledId, msg.MaxId));
                    continue;
                }

                // 비동기 처리 — gen 요청
                if (msg.Type == IpcMessageTypes.Gen)
                {
                    // 이미 cancel-batch로 무효화된 ID
                    if (msg.Id <= Interlocked.Read(ref _maxCancelledId))
                    {
                        await SendErrAsync(writer, msg.Id, "cancelled-pre-start", retryable: false);
                        continue;
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));  // 작업당 타임아웃
                    _inflight[msg.Id] = cts;
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

            // 캐시 경로: 메인이 미리 계산한 경로를 보내지 않고 워커가 직접 결정.
            // 메인은 동일한 키 알고리즘으로 같은 경로를 알고 있어야 함.
            // → ThumbnailDiskCache.GetCachePath와 동일 로직 미러 필요
            // Phase 1: 임시로 메인이 cachePath를 응답에서 받아 디스크 읽기로 사용
            // (양쪽 키 알고리즘 분기 방지)
            string cachePath = ComputeCachePath(req);
            try { Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!); } catch { }

            // 원자적 쓰기: 임시 파일 → rename
            var tmpPath = cachePath + ".tmp";
            await File.WriteAllBytesAsync(tmpPath, result.PngBytes, ct);
            try { File.Move(tmpPath, cachePath, overwrite: true); }
            catch (Exception ex) { WorkerLogger.Log($"[Worker] move failed for #{req.Id}: {ex.Message}"); }

            var ok = new IpcEnvelope
            {
                Type = IpcMessageTypes.Ok,
                Id = req.Id,
                CachePath = cachePath,
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

    /// <summary>
    /// 메인 측 ThumbnailDiskCache.GetCachePath와 동일한 키 알고리즘.
    /// 양쪽이 일치해야 메인이 캐시 hit 시 워커 호출 없이 디스크 직접 읽기 가능.
    /// </summary>
    private static string ComputeCachePath(IpcEnvelope req)
    {
        var fi = new FileInfo(req.Path!);
        long size = fi.Exists ? fi.Length : 0;
        long mtime = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;

        var keySource = string.Join("|",
            req.Path ?? "",
            size.ToString(),
            mtime.ToString(),
            req.Size.ToString(),
            req.Mode ?? "",
            req.Theme ?? "",
            req.Dpi.ToString(),
            req.ApplyExif ? "1" : "0",
            req.IsCloudOnly ? "1" : "0");

        Span<byte> hash = stackalloc byte[20];
        System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(keySource), hash);
        var sb = new StringBuilder(40);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        var hex = sb.ToString();
        return Path.Combine(_cacheDir, hex.Substring(0, 2), hex + ".png");
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
