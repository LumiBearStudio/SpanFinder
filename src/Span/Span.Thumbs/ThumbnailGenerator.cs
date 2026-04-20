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

        // ── 3. ThumbnailOptions: 클라우드 파일 가드 ──
        var options = isCloudOnly
            ? ThumbnailOptions.ReturnOnlyIfCached
            : ThumbnailOptions.UseCurrentScale;

        // ── 4. Shell 썸네일 호출 ──
        using var thumbnail = await storageFile
            .GetThumbnailAsync(tm, (uint)requestedSize, options)
            .AsTask(ct);
        ct.ThrowIfCancellationRequested();

        if (thumbnail == null || thumbnail.Type != ThumbnailType.Image)
            return null;

        // ── 5. SoftwareBitmap 디코딩 + EXIF 회전 (옵션) ──
        var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(ct);
        ct.ThrowIfCancellationRequested();

        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            return null;

        SoftwareBitmap softwareBitmap;
        if (applyExif)
        {
            // P2-4b: EXIF 회전을 PNG에 미리 굽기
            var transform = new BitmapTransform();  // 추가 변환 없음 — 회전만
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
            // ── 6. PNG 인코딩 ──
            using var memStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memStream).AsTask(ct);

            // PNG 인코더는 Bgra8 + Straight 또는 Premultiplied 모두 가능. 입력 그대로 OK.
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync().AsTask(ct);

            // 메모리 스트림 → byte[]
            memStream.Seek(0);
            var bytes = new byte[memStream.Size];
            using var reader = new DataReader(memStream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)memStream.Size).AsTask(ct);
            reader.ReadBytes(bytes);

            return new GenerateResult(
                bytes,
                (int)softwareBitmap.PixelWidth,
                (int)softwareBitmap.PixelHeight,
                applyExif);
        }
        finally
        {
            softwareBitmap.Dispose();
        }
    }
}
