using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Span.Thumbs;

/// <summary>
/// 워커 측 썸네일 생성 — 메인의 FileViewModel.LoadShellThumbnailAsync 로직 발췌 + PNG 인코딩.
///
/// 입력: filePath, requestedSize, mode, isCloudOnly, applyExif
/// 출력: PNG 바이트 (호출자가 디스크에 저장)
///
/// 주의:
/// - 클라우드 파일 가드 (P2-4c): isCloudOnly=true면 ReturnOnlyIfCached로 절대 다운로드 안 함
/// - EXIF 회전 (P2-4b): applyExif=true면 SoftwareBitmap에 회전 적용 후 PNG로 저장
/// - Shell 캐시 1차 (P2-12): 디코더 호출 회피 → 격리 효과 증폭
/// - 모든 IDisposable은 즉시 해제 (P2-11): D3D 리소스 누수 차단
/// - 워커가 죽어도 메인은 영향 없음 (이게 격리의 핵심)
/// </summary>
internal sealed class ThumbnailGenerator
{
    public sealed record GenerateResult(byte[] PngBytes, int Width, int Height, bool AppliedExif);

    public async Task<GenerateResult?> GenerateAsync(
        string filePath,
        int requestedSize,
        string mode,
        bool isCloudOnly,
        bool applyExif,
        CancellationToken ct)
    {
        // ── 0. Issue #56: .clip은 셸 썸네일 미지원 → 내장 SQLite 미리보기 추출 경로 ──
        if (string.Equals(Path.GetExtension(filePath), ".clip", StringComparison.OrdinalIgnoreCase))
            return await GenerateFromClipAsync(filePath, requestedSize, ct);

        // ── 1. StorageFile 획득 ──
        var storageFile = await StorageFile.GetFileFromPathAsync(filePath).AsTask(ct);
        ct.ThrowIfCancellationRequested();

        // ── 2. ThumbnailMode 매핑 ──
        ThumbnailMode tm = mode switch
        {
            "ListView" => ThumbnailMode.ListView,
            "DocumentsView" => ThumbnailMode.DocumentsView,
            "PicturesView" => ThumbnailMode.PicturesView,
            "VideosView" => ThumbnailMode.VideosView,
            "MusicView" => ThumbnailMode.MusicView,
            _ => ThumbnailMode.SingleItem,
        };

        // ── 3. Shell 썸네일 호출 (P2-12: 캐시 1차 → miss 시 디코더) ──
        StorageItemThumbnail? thumbnail = await GetShellThumbnailAsync(storageFile, tm, requestedSize, isCloudOnly, ct);
        if (thumbnail == null) return null;

        try
        {
            if (thumbnail.Type != ThumbnailType.Image) return null;

            // ── 4. SoftwareBitmap 디코딩 + EXIF 회전 ──
            var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(ct);
            ct.ThrowIfCancellationRequested();

            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
                return null;

            SoftwareBitmap softwareBitmap;
            if (applyExif)
            {
                // P2-4b: EXIF 회전을 PNG에 미리 굽기 (메인은 file:// 단순 로딩만 하면 됨)
                var transform = new BitmapTransform();
                softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(ct);
            }
            else
            {
                softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(ct);
            }
            ct.ThrowIfCancellationRequested();

            try
            {
                // ── 5. PNG 인코딩 → byte[] ──
                using var memStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memStream).AsTask(ct);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync().AsTask(ct);

                memStream.Seek(0);
                var bytes = new byte[memStream.Size];
                using (var reader = new DataReader(memStream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)memStream.Size).AsTask(ct);
                    reader.ReadBytes(bytes);
                }

                return new GenerateResult(
                    bytes,
                    (int)softwareBitmap.PixelWidth,
                    (int)softwareBitmap.PixelHeight,
                    applyExif);
            }
            finally
            {
                // P2-11: SoftwareBitmap 즉시 해제 (D3D surface 누수 방지)
                softwareBitmap.Dispose();
            }
        }
        finally
        {
            // P2-11: StorageItemThumbnail 즉시 해제 (RCW finalizer 의존 금지)
            thumbnail.Dispose();
        }
    }

    /// <summary>
    /// Issue #56: .clip 내장 미리보기 PNG(CanvasPreview)를 추출해 요청 크기로 축소 후 재인코딩.
    /// 원본 미리보기(예: 930x1315)를 긴 변 기준 requestedSize로 비율 유지 다운스케일한다.
    /// 셸 핸들러에 의존하지 않아 CSP 설치 여부와 무관하게 동작. 추출 실패 시 null(빈 아이콘).
    /// </summary>
    private static async Task<GenerateResult?> GenerateFromClipAsync(
        string filePath,
        int requestedSize,
        CancellationToken ct)
    {
        byte[]? previewPng = ClipThumbnailExtractor.TryExtractPreviewPng(filePath, ct);
        if (previewPng == null || previewPng.Length == 0) return null;
        ct.ThrowIfCancellationRequested();

        // 추출 PNG를 메모리 스트림에 실어 디코드
        using var srcStream = new InMemoryRandomAccessStream();
        using (var dw = new DataWriter(srcStream))
        {
            dw.WriteBytes(previewPng);
            await dw.StoreAsync().AsTask(ct);
            dw.DetachStream();
        }
        srcStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(srcStream).AsTask(ct);
        ct.ThrowIfCancellationRequested();
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;

        // 긴 변을 requestedSize로 맞춰 비율 유지 축소 (원본이 더 작으면 원본 크기 유지)
        uint w = decoder.PixelWidth, h = decoder.PixelHeight;
        double scale = Math.Min(1.0, (double)requestedSize / Math.Max(w, h));
        uint tw = Math.Max(1, (uint)Math.Round(w * scale));
        uint th = Math.Max(1, (uint)Math.Round(h * scale));

        var transform = new BitmapTransform
        {
            ScaledWidth = tw,
            ScaledHeight = th,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct);
        ct.ThrowIfCancellationRequested();

        try
        {
            // 기존 경로와 동일하게 PNG 바이트로 인코딩 → 호출자가 캐시에 저장
            using var memStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memStream).AsTask(ct);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync().AsTask(ct);

            memStream.Seek(0);
            var bytes = new byte[memStream.Size];
            using (var reader = new DataReader(memStream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)memStream.Size).AsTask(ct);
                reader.ReadBytes(bytes);
            }

            return new GenerateResult(
                bytes,
                (int)softwareBitmap.PixelWidth,
                (int)softwareBitmap.PixelHeight,
                false);
        }
        finally
        {
            // P2-11: SoftwareBitmap 즉시 해제
            softwareBitmap.Dispose();
        }
    }

    /// <summary>
    /// P2-12 (Files App 차용): Shell 캐시(thumbcache_*.db) 1차 시도 → miss 시 정식 디코더.
    /// 빠른 폴더 진입에서 hit이 많으면 디코더 호출 자체를 회피 → 격리 효과 증폭.
    /// 클라우드 파일은 무조건 ReturnOnlyIfCached (P2-4c — 다운로드 가드).
    /// </summary>
    private static async Task<StorageItemThumbnail?> GetShellThumbnailAsync(
        StorageFile file,
        ThumbnailMode mode,
        int requestedSize,
        bool isCloudOnly,
        CancellationToken ct)
    {
        // 1차: 캐시만 시도
        StorageItemThumbnail? thumb = null;
        try
        {
            thumb = await file
                .GetThumbnailAsync(mode, (uint)requestedSize, ThumbnailOptions.ReturnOnlyIfCached)
                .AsTask(ct);
            ct.ThrowIfCancellationRequested();
        }
        catch when (!ct.IsCancellationRequested)
        {
            thumb?.Dispose();
            thumb = null;
        }

        // 클라우드 파일은 1차에서 끝 (다운로드 절대 X)
        if (isCloudOnly) return thumb;

        // 캐시 hit이면 그대로 반환
        if (thumb != null && thumb.Type == ThumbnailType.Image && thumb.Size > 0)
            return thumb;

        // 2차: 정식 디코더 호출
        thumb?.Dispose();
        return await file
            .GetThumbnailAsync(mode, (uint)requestedSize, ThumbnailOptions.UseCurrentScale)
            .AsTask(ct);
    }
}
