using Span.Services.Thumbnails;

namespace Span.Tests.Services.Thumbnails;

/// <summary>
/// ThumbnailDiskCache (Phase 1, P1-5) — 캐시 키 결정성 + LRU 정리 정책 검증.
///
/// 의존성 없음 (System.IO + SHA1) → 단위 테스트 가능.
/// </summary>
[TestClass]
public class ThumbnailDiskCacheTests
{
    private static readonly DateTime FixedMtime = new(2026, 4, 20, 9, 30, 0, DateTimeKind.Utc);

    private static string Key(ThumbnailDiskCache cache,
        string path = @"C:\test\a.jpg",
        long size = 1024,
        DateTime? mtime = null,
        int reqSize = 96,
        string mode = "SingleItem",
        string theme = "Light",
        uint dpi = 96,
        bool applyExif = true,
        bool isCloudOnly = false)
        => cache.GetCachePath(path, size, mtime ?? FixedMtime, reqSize, mode, theme, dpi, applyExif, isCloudOnly);

    [TestMethod]
    public void GetCachePath_IsDeterministic()
    {
        var cache = new ThumbnailDiskCache();
        var k1 = Key(cache);
        var k2 = Key(cache);
        Assert.AreEqual(k1, k2, "동일 입력 → 동일 캐시 경로");
    }

    [TestMethod]
    public void GetCachePath_ChangesWithEachKeyField()
    {
        // P2-4a 보강: 모든 키 필드가 캐시 경로에 영향을 주는지
        var cache = new ThumbnailDiskCache();
        var baseline = Key(cache);

        Assert.AreNotEqual(baseline, Key(cache, path: @"C:\test\b.jpg"), "path 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, size: 2048), "size 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, mtime: FixedMtime.AddSeconds(1)), "mtime 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, reqSize: 128), "reqSize 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, mode: "ListView"), "mode 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, theme: "Dark"), "theme 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, dpi: 144), "dpi 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, applyExif: false), "applyExif 변경 시 키 달라야 함");
        Assert.AreNotEqual(baseline, Key(cache, isCloudOnly: true), "isCloudOnly 변경 시 키 달라야 함");
    }

    [TestMethod]
    public void GetCachePath_UnderCacheRoot()
    {
        var cache = new ThumbnailDiskCache();
        var k = Key(cache);
        Assert.IsTrue(k.StartsWith(cache.CacheRoot, StringComparison.OrdinalIgnoreCase),
            $"캐시 경로는 CacheRoot 하위여야 함. CacheRoot={cache.CacheRoot}, key={k}");
    }

    [TestMethod]
    public void GetCachePath_HasPngExtension()
    {
        var cache = new ThumbnailDiskCache();
        var k = Key(cache);
        Assert.IsTrue(k.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
            $".png 확장자 필요: {k}");
    }

    [TestMethod]
    public void GetCachePath_HasTwoCharPrefixSubdir()
    {
        // 단일 폴더 파일 수 제한 → SHA1[0..2] prefix 폴더로 분산
        var cache = new ThumbnailDiskCache();
        var k = Key(cache);
        var rel = Path.GetRelativePath(cache.CacheRoot, k);
        var parts = rel.Split(Path.DirectorySeparatorChar);
        Assert.AreEqual(2, parts.Length, $"prefix/hash.png 구조 필요: {rel}");
        Assert.AreEqual(2, parts[0].Length, $"prefix는 2자: {parts[0]}");
    }

    [TestMethod]
    public void GetCachePath_FilenameIsHexHashPlusPng()
    {
        var cache = new ThumbnailDiskCache();
        var k = Key(cache);
        var fileName = Path.GetFileNameWithoutExtension(k);
        Assert.AreEqual(40, fileName.Length, "SHA1 hex = 40자");
        Assert.IsTrue(fileName.All(c => "0123456789abcdef".Contains(c)),
            $"파일명은 hex만: {fileName}");
    }

    [TestMethod]
    public void IsCached_FalseForNonexistentPath()
    {
        var cache = new ThumbnailDiskCache();
        Assert.IsFalse(cache.IsCached(@"C:\does\not\exist.png"));
    }

    [TestMethod]
    public void IsCached_FalseForEmptyFile()
    {
        var cache = new ThumbnailDiskCache();
        var tmp = Path.Combine(Path.GetTempPath(), $"span_cache_test_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(tmp, Array.Empty<byte>());
            Assert.IsFalse(cache.IsCached(tmp), "빈 파일은 캐시로 인식 X (손상 가드)");
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [TestMethod]
    public void IsCached_TrueForNonEmptyFile()
    {
        var cache = new ThumbnailDiskCache();
        var tmp = Path.Combine(Path.GetTempPath(), $"span_cache_test_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(tmp, new byte[] { 1, 2, 3 });
            Assert.IsTrue(cache.IsCached(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [TestMethod]
    public void CleanupOldEntries_NeverThrows_OnMissingRoot()
    {
        // 빈 환경에서도 안전하게 동작 (시작 시 무조건 호출됨)
        var cache = new ThumbnailDiskCache();
        cache.CleanupOldEntries();  // throw하면 실패
    }
}
