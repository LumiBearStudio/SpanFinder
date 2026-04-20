using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Span.Services.Thumbnails;

/// <summary>
/// 워커가 만든 PNG를 디스크에 보관 + LRU 정리.
///
/// 위치: %LocalAppData%\Span\ThumbCache\{prefix}\{hash}.png
///   - prefix = hash[0..2]   (단일 폴더 파일 수 제한 분산)
///   - hash   = SHA1(path | size | mtime | reqSize | mode | theme | dpi | exif | cloud)
///
/// 정리: 7일 미접근 OR 1GB 초과 시 가장 오래된 것부터 삭제 (시작 시 1회).
/// </summary>
internal sealed class ThumbnailDiskCache
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
    private const long MaxBytesTotal = 1L * 1024 * 1024 * 1024; // 1GB

    public string CacheRoot { get; }

    public ThumbnailDiskCache()
    {
        CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Span", "ThumbCache");
        try { Directory.CreateDirectory(CacheRoot); } catch { }
    }

    /// <summary>
    /// 캐시 키 계산 + 경로 반환. 파일 존재 여부는 별도 IsCached로 확인.
    /// 캐시 키 보강 (P2-4a): theme | dpi | exifApplied | isCloudOnly 모두 포함.
    /// </summary>
    public string GetCachePath(
        string filePath,
        long fileSize,
        DateTime mtimeUtc,
        int requestedSize,
        string mode,
        string theme,
        uint dpi,
        bool applyExif,
        bool isCloudOnly)
    {
        var keySource = string.Join("|",
            filePath ?? "",
            fileSize.ToString(),
            mtimeUtc.Ticks.ToString(),
            requestedSize.ToString(),
            mode ?? "",
            theme ?? "",
            dpi.ToString(),
            applyExif ? "1" : "0",
            isCloudOnly ? "1" : "0");

        var hash = ComputeSha1Hex(keySource);
        var prefix = hash.Substring(0, 2);
        var folder = Path.Combine(CacheRoot, prefix);
        try { Directory.CreateDirectory(folder); } catch { }
        return Path.Combine(folder, hash + ".png");
    }

    public bool IsCached(string cachePath)
    {
        try { return File.Exists(cachePath) && new FileInfo(cachePath).Length > 0; }
        catch { return false; }
    }

    /// <summary>
    /// 시작 시 1회 호출 — 7일 초과 또는 1GB 초과 시 가장 오래된 것부터 삭제.
    /// 백그라운드 스레드에서 실행 권장.
    /// </summary>
    public void CleanupOldEntries()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return;

            // I8: NTFS는 Win 8 이후 LastAccessTime 업데이트 기본 비활성
            // (`fsutil behavior query disablelastaccess` → 1).
            // 우리는 캐시 hit 시 명시적으로 SetLastAccessTimeUtc 호출(GetThumbnailUriAsync)하므로
            // 활성화된 환경에서는 LRU 정확, 비활성 환경에서는 LastWriteTime처럼 동작 (= 생성 시 정렬).
            // 비활성 환경 = 7일 전에 생성된 캐시는 자주 사용해도 만료될 수 있음 → trade-off 수용.
            var files = Directory.EnumerateFiles(CacheRoot, "*.png", SearchOption.AllDirectories)
                .Select(p =>
                {
                    try { return new FileInfo(p); }
                    catch { return null; }
                })
                .Where(fi => fi != null)
                .Cast<FileInfo>()
                .OrderBy(fi => fi.LastAccessTimeUtc)  // 오래된 것 먼저
                .ToList();

            var threshold = DateTime.UtcNow - RetentionPeriod;
            long totalBytes = files.Sum(fi => SafeLength(fi));

            // 1단계: 7일 초과 삭제
            foreach (var fi in files.ToList())
            {
                if (fi.LastAccessTimeUtc >= threshold) break;  // 정렬됨 — 이후는 모두 최신
                try
                {
                    long len = fi.Length;
                    fi.Delete();
                    totalBytes -= len;
                    files.Remove(fi);
                }
                catch { }
            }

            // 2단계: 1GB 초과 시 추가 삭제 (오래된 순)
            foreach (var fi in files)
            {
                if (totalBytes <= MaxBytesTotal) break;
                try
                {
                    long len = fi.Length;
                    fi.Delete();
                    totalBytes -= len;
                }
                catch { }
            }

            // M-N2: orphan tmp 파일 정리 (워커가 cancel/crash로 남긴 .tmp.PID)
            try
            {
                foreach (var tmpFile in Directory.EnumerateFiles(CacheRoot, "*.tmp.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(tmpFile);
                        // 1시간 이상 된 tmp만 정리 (진행 중 작업 보호)
                        if (fi.LastWriteTimeUtc < DateTime.UtcNow - TimeSpan.FromHours(1))
                            fi.Delete();
                    }
                    catch { }
                }
            }
            catch { }

            // 빈 prefix 폴더 정리
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(CacheRoot))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch { }
                }
            }
            catch { }

            Helpers.DebugLogger.Log($"[ThumbCache] Cleanup done — remaining ~{totalBytes / 1024 / 1024} MB");
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ThumbCache] Cleanup failed: {ex.Message}");
        }
    }

    private static long SafeLength(FileInfo fi)
    {
        try { return fi.Length; }
        catch { return 0; }
    }

    private static string ComputeSha1Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        Span<byte> hash = stackalloc byte[20]; // SHA1 = 160bit
        SHA1.HashData(bytes, hash);
        var sb = new StringBuilder(40);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
