using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;

namespace Span.Services.Thumbnails;

/// <summary>
/// 단일 워커 프로세스 wrapper.
/// - Process.Start로 spawn + JobObject 등록
/// - NamedPipeClientStream으로 IPC 라인 read/write
/// - 헬스체크 (5초 간격 ping)
/// - 죽음 감지 + 자동 재시작은 ThumbnailClientService 책임
/// </summary>
internal sealed class WorkerProcess : IDisposable
{
    private readonly int _workerId;
    private readonly string _exePath;
    private readonly string _cacheDir;
    private readonly JobObjectHelper? _jobObject;  // I7: null 가능 — null이면 ClientService가 _disabled 처리

    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private DataReceivedEventHandler? _stderrHandler;

    private readonly ConcurrentDictionary<long, TaskCompletionSource<IpcEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _readLoopCts;

    private DateTime _lastPongUtc = DateTime.UtcNow;

    public bool IsAlive => _process is { HasExited: false } && _pipe?.IsConnected == true;
    public int WorkerId => _workerId;
    public int? Pid => _process?.Id;
    public string? PipeName { get; private set; }

    public WorkerProcess(int workerId, string exePath, string cacheDir, JobObjectHelper? sharedJob)
    {
        _workerId = workerId;
        _exePath = exePath;
        _cacheDir = cacheDir;
        _jobObject = sharedJob;
    }

    /// <summary>
    /// 워커 프로세스 spawn + Pipe 연결. 5초 내 연결 안 되면 false.
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken ct)
    {
        try
        {
            PipeName = $"Span.Thumbs.{Environment.ProcessId}.{_workerId}.{Guid.NewGuid():N}";

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? "",
            };
            psi.ArgumentList.Add("--pipe");
            psi.ArgumentList.Add(PipeName);
            psi.ArgumentList.Add("--cache");
            psi.ArgumentList.Add(_cacheDir);

            _process = Process.Start(psi);
            if (_process == null)
            {
                DebugLogger.Log($"[WorkerProcess#{_workerId}] Process.Start returned null");
                return false;
            }

            // JobObject 등록 — 메인 종료 시 워커도 자동 종료 (orphan 방지)
            // C-LK2: 실패 시 abort — orphan 워커 위험 차단 (메인 강제종료 시 워커 30초 살아남음)
            // I7: _jobObject가 null이면 ClientService에서 _disabled로 차단됐을 것이지만 방어
            if (_jobObject == null)
            {
                DebugLogger.Log($"[WorkerProcess#{_workerId}] No JobObject (init failed) → aborting spawn");
                SafeKill();
                return false;
            }
            bool jobAssigned;
            try { jobAssigned = _jobObject.AssignProcess(_process); }
            catch (Exception ex)
            {
                jobAssigned = false;
                DebugLogger.Log($"[WorkerProcess#{_workerId}] JobObject assign threw: {ex.Message}");
            }
            if (!jobAssigned)
            {
                DebugLogger.Log($"[WorkerProcess#{_workerId}] JobObject not assigned → orphan risk, aborting spawn");
                SafeKill();
                return false;
            }

            // 워커 stderr 라인은 메인 로그로 mirror
            // M6: 핸들러를 필드에 보관 → Dispose 시 명시적 -=
            _stderrHandler = (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    DebugLogger.Log($"[Worker#{_workerId}.stderr] {e.Data}");
            };
            _process.ErrorDataReceived += _stderrHandler;
            try { _process.BeginErrorReadLine(); } catch { }

            // Pipe 연결 (최대 5초)
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                try { await _pipe.ConnectAsync(connectCts.Token); }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[WorkerProcess#{_workerId}] pipe connect failed: {ex.Message}");
                    // C-LK1: pipe 명시 dispose — Dispose 경로 200ms write-lock 대기 우회
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                    SafeKill();
                    return false;
                }
            }

            _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

            _readLoopCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(_readLoopCts.Token));

            _lastPongUtc = DateTime.UtcNow;
            DebugLogger.Log($"[WorkerProcess#{_workerId}] Started (pid={_process.Id}, pipe={PipeName})");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[WorkerProcess#{_workerId}] StartAsync failed: {ex.Message}");
            SafeKill();
            return false;
        }
    }

    /// <summary>
    /// Generate request 전송 + ok/err 응답 대기.
    /// C1: ID는 호출자가 발급한 글로벌 값 그대로 사용 — cancel-batch 매칭 보장.
    /// I5: timeout cancel 시 워커에 추가 cancel 메시지 전송 안 함 (이미 응답 못함).
    /// C-N2: 진입/등록 후 _disposed 재확인 — 종료 시점 ObjectDisposedException 방지.
    /// </summary>
    public async Task<IpcEnvelope> RequestAsync(IpcEnvelope request, CancellationToken ct)
    {
        // C-N2: 진입 시점 dispose 검사 — InvalidOperationException으로 통일 (I6 retry 매칭)
        if (System.Threading.Volatile.Read(ref _disposed) != 0)
            throw new InvalidOperationException("worker disposed");
        if (!IsAlive) throw new InvalidOperationException("worker not alive");
        if (request.Id <= 0) throw new ArgumentException("request.Id must be set by caller (global)", nameof(request));

        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;

        // C-N2 + C-3R-3: 등록 직후 재확인 — Dispose가 _pending 순회 전에 도착할 수 있음
        // tcs를 unobserved로 두면 TaskScheduler.UnobservedTaskException 트리거 (Sentry 노이즈)
        // → SetException으로 observe 처리 후 throw
        if (System.Threading.Volatile.Read(ref _disposed) != 0)
        {
            if (_pending.TryRemove(request.Id, out var orphan))
                orphan.TrySetException(new InvalidOperationException("worker disposed during registration"));
            throw new InvalidOperationException("worker disposed during registration");
        }

        await SendAsync(request);

        using var reg = ct.Register(() =>
        {
            if (_pending.TryRemove(request.Id, out var t))
                t.TrySetCanceled(ct);
            // 워커에 cancel 요청 — 살아있을 때만 (죽은 워커에 보내봐야 ObjectDisposedException 노이즈)
            if (IsAlive && System.Threading.Volatile.Read(ref _disposed) == 0)
                _ = SendAsync(new IpcEnvelope { Type = IpcMessageTypes.Cancel, Id = request.Id });
        });

        return await tcs.Task;
    }

    /// <summary>
    /// Cancel batch — 폴더 이동 시 진행 중 모든 요청 무효화.
    /// </summary>
    public Task CancelBatchAsync(long minId, long maxId)
    {
        // 메인 측에서도 pending 큐 정리
        foreach (var key in _pending.Keys)
        {
            if (key >= minId && key <= maxId && _pending.TryRemove(key, out var tcs))
                tcs.TrySetException(new OperationCanceledException("cancel-batch"));
        }
        return SendAsync(new IpcEnvelope { Type = IpcMessageTypes.CancelBatch, MinId = minId, MaxId = maxId });
    }

    /// <summary>헬스체크 — 마지막 pong이 N초 이상 지났으면 죽음으로 판단.</summary>
    public bool IsHealthy(TimeSpan tolerance)
    {
        if (!IsAlive) return false;
        return (DateTime.UtcNow - _lastPongUtc) <= tolerance;
    }

    public Task SendPingAsync()
        => SendAsync(new IpcEnvelope { Type = IpcMessageTypes.Ping });

    public Task SendShutdownAsync()
        => SendAsync(new IpcEnvelope { Type = IpcMessageTypes.Shutdown });

    private async Task SendAsync(IpcEnvelope msg)
    {
        if (_writer == null) return;
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var json = JsonSerializer.Serialize(msg, IpcJson.Options);
                await _writer.WriteLineAsync(json).ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }
        catch (Exception ex) { DebugLogger.Log($"[WorkerProcess#{_workerId}] send failed: {ex.Message}"); }
    }

    // M8: 라인 크기 상한 — 워커가 비정상으로 거대 메시지 보내면 메모리 폭증 방지
    private const int MaxLineLength = 64 * 1024;

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await _reader!.ReadLineAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[WorkerProcess#{_workerId}] read failed: {ex.Message}");
                    break;
                }

                if (line == null)
                {
                    DebugLogger.Log($"[WorkerProcess#{_workerId}] pipe EOF");
                    break;
                }
                if (line.Length == 0) continue;
                if (line.Length > MaxLineLength)
                {
                    DebugLogger.Log($"[WorkerProcess#{_workerId}] discarded oversized line ({line.Length} bytes)");
                    continue;
                }

                IpcEnvelope? msg = null;
                try { msg = JsonSerializer.Deserialize<IpcEnvelope>(line, IpcJson.Options); }
                catch (Exception ex) { DebugLogger.Log($"[WorkerProcess#{_workerId}] parse failed: {ex.Message}"); continue; }
                if (msg == null) continue;

                if (msg.Type == IpcMessageTypes.Pong)
                {
                    _lastPongUtc = DateTime.UtcNow;
                    continue;
                }

                if (msg.Type == IpcMessageTypes.Ok || msg.Type == IpcMessageTypes.Err)
                {
                    if (_pending.TryRemove(msg.Id, out var tcs))
                        tcs.TrySetResult(msg);
                    continue;
                }
            }
        }
        finally
        {
            // 읽기 종료 → 모든 pending 실패 처리
            foreach (var key in _pending.Keys)
            {
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetException(new IOException("worker disconnected"));
            }
        }
    }

    private void SafeKill()
    {
        try
        {
            if (_process != null && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private int _disposed;  // 0 = alive, 1 = disposed (Interlocked)

    /// <summary>
    /// C5: write lock 잡고 진행하여 동시 SendAsync와 race 차단.
    /// 한 번만 실행되도록 Interlocked 가드.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;  // idempotent

        // 진행 중 write가 끝날 때까지 짧게 대기 (max 200ms — UI 블록 방지)
        bool gotLock = false;
        try { gotLock = _writeLock.Wait(TimeSpan.FromMilliseconds(200)); } catch { }

        try
        {
            try { _readLoopCts?.Cancel(); } catch { }
            try { _readLoopCts?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }

            // 모든 pending TCS 해제
            foreach (var key in _pending.Keys)
            {
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetException(new ObjectDisposedException(nameof(WorkerProcess)));
            }

            SafeKill();

            // M6: stderr 핸들러 명시 해제 (Process.Dispose가 처리하지만 보수적으로)
            try
            {
                if (_process != null && _stderrHandler != null)
                    _process.ErrorDataReceived -= _stderrHandler;
            }
            catch { }

            try { _process?.Dispose(); } catch { }
        }
        finally
        {
            if (gotLock) { try { _writeLock.Release(); } catch { } }
            try { _writeLock.Dispose(); } catch { }
        }
    }
}
