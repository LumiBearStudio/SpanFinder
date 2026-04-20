using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;

namespace Span.Services.Thumbnails;

/// <summary>
/// 메인 측 썸네일 클라이언트 — 워커 풀 (Phase 2: 2개) + 캐시 라우팅 + 폴백.
///
/// API:
///   var uri = await client.GetThumbnailUriAsync(filePath, size, ...);
///   if (uri != null) bitmap.UriSource = uri;  // 캐시 hit 또는 워커 생성
///   else { 기존 인프로세스 경로 폴백 }
///
///   await client.CancelBatchAsync();  // 폴더 이동 시 진행 중 모든 요청 무효화
///
/// Phase 2:
/// - 워커 풀 2개 + 라운드로빈 (P2-1)
/// - 워커 죽음 감지 + 백오프 재spawn 1s/3s/10s (P2-2)
/// - Cancel batch (P2-3)
/// - 폴더 단위 폴백 (P2-5)
/// - 적응형 타임아웃 cold 7s / warm 3s (P2-6)
/// </summary>
public sealed class ThumbnailClientService : IDisposable
{
    private const int PoolSize = 2;
    private const int FolderFailureThreshold = 3;       // 같은 폴더 N회 실패 시 해당 폴더 인프로세스 폴백
    private const int SessionFailureThreshold = 20;     // 누적 실패 N회 → 세션 동안 영구 비활성

    private readonly object _lock = new();
    private readonly ThumbnailDiskCache _cache = new();
    private readonly JobObjectHelper _jobObject;
    private readonly WorkerProcess?[] _workers = new WorkerProcess?[PoolSize];
    private readonly int[] _workerFailures = new int[PoolSize];
    private readonly DateTime[] _nextRetryAt = new DateTime[PoolSize];
    private readonly bool[] _workerWarm = new bool[PoolSize];
    private int _roundRobinIndex;
    private readonly string _workerExePath;
    private bool _spawnAttempted;
    private bool _disabled;
    private int _totalFailures;
    private long _idCounter;

    // 폴더 단위 폴백 (P2-5) — 동일 폴더에서 N회 연속 실패 시 해당 폴더 일시 차단
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _folderFailureCount = new();

    public ThumbnailClientService()
    {
        _jobObject = new JobObjectHelper();

        var baseDir = AppContext.BaseDirectory;
        _workerExePath = Path.Combine(baseDir, "Span.Thumbs.exe");
        if (!File.Exists(_workerExePath))
        {
            var alt = Path.Combine(baseDir, "Span.Thumbs", "Span.Thumbs.exe");
            if (File.Exists(alt)) _workerExePath = alt;
        }

        // 시작 시 캐시 정리 (백그라운드)
        Task.Run(() => _cache.CleanupOldEntries());
    }

    /// <summary>
    /// 썸네일 요청 — 캐시 hit 시 즉시 경로 반환, miss 시 워커 호출.
    /// 실패 또는 워커 미동작 시 null → 호출자가 인프로세스 폴백.
    /// </summary>
    public async Task<Uri?> GetThumbnailUriAsync(
        string filePath,
        int requestedSize,
        string mode,
        bool isCloudOnly,
        bool applyExif,
        string theme,
        uint dpi,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        if (_disabled) return null;

        // ── 1. 폴더 단위 폴백 검사 (P2-5) ──
        var parentDir = SafeGetDirectoryName(filePath);
        if (parentDir != null && _folderFailureCount.TryGetValue(parentDir, out var folderFails)
            && folderFails >= FolderFailureThreshold)
        {
            return null;  // 이 폴더는 인프로세스 폴백
        }

        // ── 2. 캐시 키 계산 ──
        long fileSize = 0;
        DateTime mtimeUtc = DateTime.MinValue;
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return null;
            fileSize = fi.Length;
            mtimeUtc = fi.LastWriteTimeUtc;
        }
        catch { return null; }

        var cachePath = _cache.GetCachePath(
            filePath, fileSize, mtimeUtc,
            requestedSize, mode, theme, dpi, applyExif, isCloudOnly);

        // ── 3. 캐시 hit ──
        if (_cache.IsCached(cachePath))
        {
            try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); }
            catch { }
            return new Uri(cachePath);
        }

        // ── 4. 워커 보장 (라운드로빈) ──
        var worker = await EnsureWorkerAsync(ct).ConfigureAwait(false);
        if (worker == null) return null;

        // ── 5. 적응형 타임아웃 (P2-6) ──
        // cold start (warm 안 된 워커): 7초, warm: 3초
        var workerId = worker.WorkerId;
        bool isWarm = _workerWarm[workerId];
        var timeout = isWarm ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(7);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        // ── 6. 워커 호출 ──
        var req = new IpcEnvelope
        {
            Type = IpcMessageTypes.Gen,
            Path = filePath,
            Size = requestedSize,
            Mode = mode,
            IsCloudOnly = isCloudOnly,
            ApplyExif = applyExif,
            Theme = theme,
            Dpi = dpi,
        };
        // ID는 풀 전역에서 고유 (cancel-batch에서 사용)
        req.Id = Interlocked.Increment(ref _idCounter);

        try
        {
            var resp = await worker.RequestAsync(req, timeoutCts.Token).ConfigureAwait(false);
            if (resp.Type == IpcMessageTypes.Ok && !string.IsNullOrEmpty(resp.CachePath) && File.Exists(resp.CachePath))
            {
                // 첫 성공 → warm 표시
                _workerWarm[workerId] = true;
                // 폴더/워커 실패 카운터 리셋
                if (parentDir != null) _folderFailureCount.TryRemove(parentDir, out _);
                _workerFailures[workerId] = 0;
                return new Uri(resp.CachePath);
            }

            // err — 단순 실패. 폴더 카운터만 증가, 워커는 살아있음
            if (parentDir != null)
            {
                _folderFailureCount.AddOrUpdate(parentDir, 1, (_, n) => n + 1);
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 호출자 취소 — 워커 책임 아님
            throw;
        }
        catch (OperationCanceledException)
        {
            // 타임아웃 — 워커 응답 없음, 워커 죽었을 가능성
            DebugLogger.Log($"[ThumbnailClient] worker#{workerId} timeout (warm={isWarm})");
            HandleWorkerFailure(workerId);
            if (parentDir != null)
                _folderFailureCount.AddOrUpdate(parentDir, 1, (_, n) => n + 1);
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[ThumbnailClient] worker#{workerId} call failed: {ex.Message}");
            HandleWorkerFailure(workerId);
            if (parentDir != null)
                _folderFailureCount.AddOrUpdate(parentDir, 1, (_, n) => n + 1);
            return null;
        }
    }

    /// <summary>
    /// P2-3: 폴더 이동 등으로 진행 중인 모든 요청 무효화.
    /// 호출자(FileViewModel/MainViewModel)가 폴더 변경 시 호출 권장.
    /// </summary>
    public Task CancelAllInflightAsync()
    {
        var maxId = Interlocked.Read(ref _idCounter);
        var tasks = new System.Collections.Generic.List<Task>(PoolSize);
        for (int i = 0; i < PoolSize; i++)
        {
            var w = _workers[i];
            if (w != null && w.IsAlive)
            {
                try { tasks.Add(w.CancelBatchAsync(0, maxId)); }
                catch { }
            }
        }
        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// 폴더 단위 폴백 카운터 리셋 — 호출자가 명시적으로 회복하고 싶을 때.
    /// </summary>
    public void ResetFolderFailures(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        _folderFailureCount.TryRemove(folderPath, out _);
    }

    private async Task<WorkerProcess?> EnsureWorkerAsync(CancellationToken ct)
    {
        // 라운드로빈 시도 — 살아있는 워커 우선
        int startIndex = Interlocked.Increment(ref _roundRobinIndex) % PoolSize;
        for (int offset = 0; offset < PoolSize; offset++)
        {
            int idx = (startIndex + offset) % PoolSize;
            var w = _workers[idx];
            if (w != null && w.IsAlive) return w;
        }

        // 모두 죽음 → 라운드로빈 시작점부터 spawn 시도
        for (int offset = 0; offset < PoolSize; offset++)
        {
            int idx = (startIndex + offset) % PoolSize;
            // 백오프 중인 슬롯 skip
            if (DateTime.UtcNow < _nextRetryAt[idx]) continue;

            var spawned = await TrySpawnWorkerAsync(idx, ct).ConfigureAwait(false);
            if (spawned != null) return spawned;
        }

        return null;
    }

    private async Task<WorkerProcess?> TrySpawnWorkerAsync(int workerId, CancellationToken ct)
    {
        WorkerProcess? created = null;
        bool needSpawn;
        lock (_lock)
        {
            if (_disabled) return null;
            var existing = _workers[workerId];
            if (existing != null && existing.IsAlive) return existing;

            try { existing?.Dispose(); } catch { }
            _workers[workerId] = null;

            if (!File.Exists(_workerExePath))
            {
                if (!_spawnAttempted)
                {
                    DebugLogger.Log($"[ThumbnailClient] worker exe not found: {_workerExePath}");
                    _spawnAttempted = true;
                }
                _disabled = true;
                return null;
            }

            created = new WorkerProcess(workerId, _workerExePath, _cache.CacheRoot, _jobObject);
            needSpawn = true;
        }

        if (needSpawn && created != null)
        {
            var ok = await created.StartAsync(ct).ConfigureAwait(false);
            if (!ok)
            {
                created.Dispose();
                HandleWorkerFailure(workerId);
                return null;
            }
            lock (_lock)
            {
                _workers[workerId] = created;
                _workerWarm[workerId] = false;  // 새로 spawn → cold
            }
            return created;
        }

        return null;
    }

    private void HandleWorkerFailure(int workerId)
    {
        _workerFailures[workerId]++;
        _totalFailures++;

        // 백오프: 1s → 3s → 10s
        var delay = _workerFailures[workerId] switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(10),
        };
        _nextRetryAt[workerId] = DateTime.UtcNow + delay;

        // 누적 실패 임계 초과 → 세션 영구 비활성
        if (_totalFailures >= SessionFailureThreshold)
        {
            DebugLogger.Log($"[ThumbnailClient] disabled — total failures {_totalFailures} exceeded threshold");
            _disabled = true;
        }

        lock (_lock)
        {
            try { _workers[workerId]?.Dispose(); } catch { }
            _workers[workerId] = null;
            _workerWarm[workerId] = false;
        }
    }

    private static string? SafeGetDirectoryName(string filePath)
    {
        try { return Path.GetDirectoryName(filePath); }
        catch { return null; }
    }

    public void Dispose()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            try { _workers[i]?.SendShutdownAsync().Wait(TimeSpan.FromMilliseconds(500)); } catch { }
            try { _workers[i]?.Dispose(); } catch { }
        }
        try { _jobObject.Dispose(); } catch { }
    }
}
