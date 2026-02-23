using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Span.Services
{
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
    }
}
