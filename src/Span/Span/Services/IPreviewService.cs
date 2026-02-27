using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Span.Services
{
    /// <summary>
    /// 미리보기 패널 서비스 인터페이스. 파일 확장자에 따라 미리보기 유형을 결정하고,
    /// 이미지/텍스트/PDF/미디어/Hex/폰트 등의 미리보기 데이터를 비동기로 로드한다.
    /// </summary>
    public interface IPreviewService
    {
        PreviewType GetPreviewType(string? filePath, bool isFolder);
        FilePreviewMetadata GetBasicMetadata(string filePath);
        int GetFolderItemCount(string folderPath);
        Task<BitmapImage?> LoadImagePreviewAsync(string filePath, uint maxSize, CancellationToken ct);
        Task<string?> LoadTextPreviewAsync(string filePath, CancellationToken ct);
        Task<BitmapImage?> LoadPdfPreviewAsync(string filePath, CancellationToken ct);
        Task<MediaSource?> LoadMediaSourceAsync(string filePath, CancellationToken ct);
        Task<ImageMetadata?> GetImageMetadataAsync(string filePath, CancellationToken ct);
        Task<MediaMetadata?> GetMediaMetadataAsync(string filePath, CancellationToken ct);
        Task<string?> LoadHexPreviewAsync(string filePath, CancellationToken ct);
        FontPreviewData? GetFontPreviewData(string filePath);
    }
}
