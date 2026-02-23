using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using Windows.Data.Pdf;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Span.Services
{
    public class PreviewService : IPreviewService
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".ico"
        };

        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".cs", ".json", ".xml", ".md", ".log", ".ini", ".cfg", ".yaml", ".yml",
            ".toml", ".html", ".htm", ".css", ".js", ".ts", ".py", ".java", ".cpp", ".c",
            ".h", ".go", ".rs", ".sh", ".bat", ".ps1", ".sql", ".csv", ".tsv", ".gitignore",
            ".editorconfig", ".env", ".dockerfile", ".xaml", ".csproj", ".sln"
        };

        private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mp3", ".wav", ".wma", ".avi", ".mkv", ".flac", ".ogg", ".aac",
            ".m4a", ".m4v", ".mov", ".wmv", ".webm"
        };

        private const long MaxPreviewFileSize = 100 * 1024 * 1024; // 100MB
        private const int MaxTextChars = 50000;

        public PreviewType GetPreviewType(string? filePath, bool isFolder)
        {
            if (isFolder) return PreviewType.Folder;
            if (string.IsNullOrEmpty(filePath)) return PreviewType.None;

            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return PreviewType.Generic;

            if (ImageExtensions.Contains(ext)) return PreviewType.Image;
            if (TextExtensions.Contains(ext)) return PreviewType.Text;
            if (PdfExtensions.Contains(ext)) return PreviewType.Pdf;
            if (MediaExtensions.Contains(ext)) return PreviewType.Media;

            return PreviewType.Generic;
        }

        public FilePreviewMetadata GetBasicMetadata(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                return new FilePreviewMetadata
                {
                    FileName = fi.Name,
                    Size = fi.Length,
                    Created = fi.CreationTime,
                    Modified = fi.LastWriteTime,
                    Extension = fi.Extension,
                    IsReadOnly = fi.IsReadOnly
                };
            }
            catch
            {
                return new FilePreviewMetadata { FileName = Path.GetFileName(filePath) };
            }
        }

        public int GetFolderItemCount(string folderPath)
        {
            try
            {
                int count = 0;
                var di = new DirectoryInfo(folderPath);
                foreach (var entry in di.EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                    if ((entry.Attributes & System.IO.FileAttributes.System) != 0) continue;
                    count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<BitmapImage?> LoadImagePreviewAsync(string filePath, uint maxSize, CancellationToken ct)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length > MaxPreviewFileSize) return null;

                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();

                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem, maxSize, ThumbnailOptions.UseCurrentScale);

                ct.ThrowIfCancellationRequested();

                if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    return bitmap;
                }

                // Fallback: load full image
                using var stream = await file.OpenReadAsync();
                ct.ThrowIfCancellationRequested();

                var fullBitmap = new BitmapImage();
                await fullBitmap.SetSourceAsync(stream);
                return fullBitmap;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] Image load error: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> LoadTextPreviewAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var fi = new FileInfo(filePath);
                long readSize = Math.Min(fi.Length, MaxTextChars * 2); // approximate

                using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[MaxTextChars];
                int charsRead = await reader.ReadAsync(buffer, 0, MaxTextChars);

                ct.ThrowIfCancellationRequested();

                var text = new string(buffer, 0, charsRead);
                if (charsRead == MaxTextChars && !reader.EndOfStream)
                {
                    text += "\n\n[미리보기 잘림...]";
                }

                return text;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] Text load error: {ex.Message}");
                return null;
            }
        }

        public async Task<BitmapImage?> LoadPdfPreviewAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length > MaxPreviewFileSize) return null;

                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();

                var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
                if (pdfDoc.PageCount == 0) return null;

                using var page = pdfDoc.GetPage(0);
                using var stream = new InMemoryRandomAccessStream();

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = 1024,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };

                await page.RenderToStreamAsync(stream, options);
                ct.ThrowIfCancellationRequested();

                var bitmap = new BitmapImage();
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] PDF load error: {ex.Message}");
                return null;
            }
        }

        public async Task<MediaSource?> LoadMediaSourceAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();
                return MediaSource.CreateFromStorageFile(file);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] Media load error: {ex.Message}");
                return null;
            }
        }

        public async Task<ImageMetadata?> GetImageMetadataAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();

                var props = await file.Properties.GetImagePropertiesAsync();
                ct.ThrowIfCancellationRequested();

                return new ImageMetadata(props.Width, props.Height, props.DateTaken,
                    props.CameraManufacturer, props.CameraModel);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }

        public async Task<MediaMetadata?> GetMediaMetadataAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();

                var contentType = file.ContentType;

                if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    var props = await file.Properties.GetVideoPropertiesAsync();
                    ct.ThrowIfCancellationRequested();
                    return new MediaMetadata(props.Duration, props.Bitrate, props.Width, props.Height, null, null);
                }

                if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    var props = await file.Properties.GetMusicPropertiesAsync();
                    ct.ThrowIfCancellationRequested();
                    return new MediaMetadata(props.Duration, props.Bitrate, null, null, props.Artist, props.Album);
                }

                // Fallback: try by extension
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext is ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".m4v" or ".webm")
                {
                    var props = await file.Properties.GetVideoPropertiesAsync();
                    ct.ThrowIfCancellationRequested();
                    return new MediaMetadata(props.Duration, props.Bitrate, props.Width, props.Height, null, null);
                }
                else
                {
                    var props = await file.Properties.GetMusicPropertiesAsync();
                    ct.ThrowIfCancellationRequested();
                    return new MediaMetadata(props.Duration, props.Bitrate, null, null, props.Artist, props.Album);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }
    }

    public record FilePreviewMetadata
    {
        public string FileName { get; init; } = "";
        public long Size { get; init; }
        public DateTime Created { get; init; }
        public DateTime Modified { get; init; }
        public string Extension { get; init; } = "";
        public bool IsReadOnly { get; init; }

        public string SizeFormatted => FormatBytes(Size);

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public record ImageMetadata(uint Width, uint Height, DateTimeOffset? DateTaken,
                                 string? CameraManufacturer, string? CameraModel);

    public record MediaMetadata(TimeSpan Duration, uint Bitrate,
                                 uint? Width, uint? Height,
                                 string? Artist, string? Album);
}
