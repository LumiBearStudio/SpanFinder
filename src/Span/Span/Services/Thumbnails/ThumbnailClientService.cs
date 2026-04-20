using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Helpers;

namespace Span.Services.Thumbnails;

/// <summary>
/// 메인 측 썸네일 클라이언트 — 워커 풀 관리 + 캐시 라우팅 + 폴백.
///
/// API:
///   var thumb = await client.GetThumbnailUriAsync(filePath, size, ...);
///   if (thumb != null) bitmap.UriSource = thumb;  // 캐시 hit 또는 워커 생성
///   else { 기존 인프로세스 경로 폴백 }
///
/// Phase 1: feature flag OFF 기본값. 호출자(FileViewModel/PreviewService)가 flag 검사 후
/// 진입하므로 이 서비스는 활성 상태에서만 호출됨.
///
/// 단일 워커 (Phase 1) → 워커 풀 확장은 Phase 2.
/// </summary>
public sealed class ThumbnailClientService : IDisposable
{
    private readonly object _lock = new();
    private readonly ThumbnailDiskCache _cache = new();
    private readonly JobObjectHelper _jobObject;
    private WorkerProcess? _worker;
    private readonly string _workerExePath;
    private bool _spawnAttempted;
    private bool _disabled;
    private int _consecutiveFailures;
    private DateTime _nextRetryAt = DateTime.MinValue;

    public ThumbnailClientService()
    {
        _jobObject = new JobObjectHelper();

        // 워커 exe 경로: 메인 출력 폴더 직속 (WindowsAppSDK deps 공유 위해 같은 폴더 사용)
        // 폴백: Span.Thumbs/ 하위 (구조 변경 시)
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

        // ── 1. 캐시 키 계산 ──
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

        // ── 2. 캐시 hit ──
        if (_cache.IsCached(cachePath))
        {
            try
            {
                // 접근 시간 갱신 → LRU 정확도
                File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
            }
            catch { }
            return new Uri(cachePath);
        }

        // ── 3. 워커 보장 ──
        var worker = await EnsureWorkerAsync(ct).ConfigureAwait(false);
        if (worker == null) return null;

        // ── 4. 워커 호출 ──
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

        try
        {
            var resp = await worker.RequestAsync(req, ct).ConfigureAwait(false);
            if (resp.Type == IpcMessageTypes.Ok && !string.IsNullOrEmpty(resp.CachePath) && File.Exists(resp.CachePath))
            {
                _consecutiveFailures = 0;
                return new Uri(resp.CachePath);
            }

            // err — 단순 실패 (재시도 불가) → null 반환 → 호출자 폴백
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DebugLogger.Log($"[ThumbnailClient] worker call failed: {ex.Message}");
            HandleWorkerFailure();
            return null;
        }
    }

    private async Task<WorkerProcess?> EnsureWorkerAsync(CancellationToken ct)
    {
        // Lock-free fast path
        var w = _worker;
        if (w != null && w.IsAlive) return w;

        // 백오프 중이면 즉시 null
        if (DateTime.UtcNow < _nextRetryAt) return null;

        WorkerProcess? created = null;
        bool needSpawn;
        lock (_lock)
        {
            if (_disabled) return null;
            if (_worker != null && _worker.IsAlive) return _worker;

            try { _worker?.Dispose(); } catch { }
            _worker = null;

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

            created = new WorkerProcess(workerId: 0, _workerExePath, _cache.CacheRoot, _jobObject);
            needSpawn = true;
        }

        if (needSpawn && created != null)
        {
            var ok = await created.StartAsync(ct).ConfigureAwait(false);
            if (!ok)
            {
                created.Dispose();
                HandleWorkerFailure();
                return null;
            }
            lock (_lock)
            {
                _worker = created;
            }
            return created;
        }

        return null;
    }

    private void HandleWorkerFailure()
    {
        _consecutiveFailures++;
        // 백오프: 1s → 3s → 10s → 영구 비활성
        var delay = _consecutiveFailures switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(3),
            3 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.Zero,
        };
        if (_consecutiveFailures > 3)
        {
            DebugLogger.Log("[ThumbnailClient] disabled — too many consecutive failures");
            _disabled = true;
            return;
        }
        _nextRetryAt = DateTime.UtcNow + delay;
        lock (_lock)
        {
            try { _worker?.Dispose(); } catch { }
            _worker = null;
        }
    }

    public void Dispose()
    {
        try { _worker?.SendShutdownAsync().Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _worker?.Dispose(); } catch { }
        try { _jobObject.Dispose(); } catch { }
    }
}
