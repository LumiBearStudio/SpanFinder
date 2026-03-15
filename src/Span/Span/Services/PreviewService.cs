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
    /// <summary>
    /// 미리보기 서비스 구현. 파일 확장자에 따라 미리보기 유형(Image/Text/PDF/Media/Hex/Font)을 결정하고,
    /// 각 유형별 비동기 로더(썸네일, 텍스트 읽기, PDF 렌더링, MediaSource, Hex 덤프, 폰트 파싱)를 제공.
    /// 클라우드 전용 파일은 다운로드 트리거를 방지한다.
    /// </summary>
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

        private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ttf", ".otf", ".woff", ".woff2", ".ttc"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
            ".tgz", ".tbz2", ".txz", ".cab"
        };

        private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".sys", ".bin", ".dat", ".so", ".dylib", ".o", ".obj",
            ".class", ".pyc", ".pdb", ".lib", ".a", ".wasm"
        };

        private const long MaxPreviewFileSize = 100 * 1024 * 1024; // 100MB
        private const int MaxTextChars = 50000;
        private const int HexPreviewBytes = 512; // Hex viewer: first 512 bytes

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
            if (FontExtensions.Contains(ext)) return PreviewType.Font;
            if (ArchiveExtensions.Contains(ext)) return PreviewType.Archive;
            if (BinaryExtensions.Contains(ext)) return PreviewType.HexBinary;

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

                bool isCloudOnly = CloudSyncService.IsCloudOnlyFile(filePath);

                var file = await StorageFile.GetFileFromPathAsync(filePath);
                ct.ThrowIfCancellationRequested();

                // Cloud-only: use cached thumbnail only to avoid triggering download
                var thumbOptions = isCloudOnly
                    ? ThumbnailOptions.ReturnOnlyIfCached
                    : ThumbnailOptions.UseCurrentScale;

                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem, maxSize, thumbOptions);

                ct.ThrowIfCancellationRequested();

                if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    return bitmap;
                }

                // Fallback: load full image (skip for cloud-only files to prevent download)
                if (isCloudOnly) return null;

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

        /// <summary>
        /// Read the first N bytes of a binary file and format as hex dump.
        /// Format: offset  hex bytes (16 per row)  ASCII representation
        /// </summary>
        public async Task<string?> LoadHexPreviewAsync(string filePath, CancellationToken ct)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length == 0) return "[빈 파일]";

                int bytesToRead = (int)Math.Min(fi.Length, HexPreviewBytes);
                var buffer = new byte[bytesToRead];

                using var stream = File.OpenRead(filePath);
                int read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);

                ct.ThrowIfCancellationRequested();

                var sb = new System.Text.StringBuilder((read / 16 + 1) * 80);
                for (int i = 0; i < read; i += 16)
                {
                    // Offset
                    sb.Append(i.ToString("X8"));
                    sb.Append("  ");

                    // Hex bytes
                    int lineLen = Math.Min(16, read - i);
                    for (int j = 0; j < 16; j++)
                    {
                        if (j < lineLen)
                        {
                            sb.Append(buffer[i + j].ToString("X2"));
                            sb.Append(' ');
                        }
                        else
                        {
                            sb.Append("   ");
                        }
                        if (j == 7) sb.Append(' '); // mid-separator
                    }

                    sb.Append(' ');

                    // ASCII
                    for (int j = 0; j < lineLen; j++)
                    {
                        byte b = buffer[i + j];
                        sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
                    }

                    sb.AppendLine();
                }

                if (fi.Length > HexPreviewBytes)
                    sb.AppendLine($"\n[{fi.Length:N0} bytes 중 처음 {HexPreviewBytes} bytes 표시]");

                return sb.ToString();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] Hex load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get font metadata by parsing the TrueType/OpenType 'name' table
        /// to extract the font family name for FontFamily binding.
        /// </summary>
        public FontPreviewData? GetFontPreviewData(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists || fi.Length > MaxPreviewFileSize) return null;

                var familyName = ExtractFontFamilyName(filePath);

                return new FontPreviewData
                {
                    FilePath = filePath,
                    FamilyName = familyName ?? fi.Name,
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    Extension = fi.Extension.ToUpperInvariant().TrimStart('.')
                };
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewService] Font preview error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse TrueType/OpenType 'name' table to extract font family name (nameID=1).
        /// Prefers Windows platform (UTF-16 BE), falls back to Mac (ASCII).
        /// </summary>
        private static string? ExtractFontFamilyName(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                if (stream.Length < 12) return null;

                // Offset table header
                reader.ReadBytes(4); // sfVersion
                ushort numTables = ReadUInt16BE(reader);
                reader.ReadBytes(6); // searchRange, entrySelector, rangeShift

                // Find 'name' table
                uint nameTableOffset = 0;
                for (int i = 0; i < numTables; i++)
                {
                    if (stream.Position + 16 > stream.Length) return null;
                    byte[] tagBytes = reader.ReadBytes(4);
                    string tag = System.Text.Encoding.ASCII.GetString(tagBytes);
                    reader.ReadBytes(4); // checksum
                    uint offset = ReadUInt32BE(reader);
                    reader.ReadBytes(4); // length

                    if (tag == "name")
                    {
                        nameTableOffset = offset;
                        break;
                    }
                }

                if (nameTableOffset == 0) return null;

                // Read name table header
                stream.Seek(nameTableOffset, SeekOrigin.Begin);
                if (stream.Position + 6 > stream.Length) return null;
                reader.ReadBytes(2); // format
                ushort nameCount = ReadUInt16BE(reader);
                ushort stringOffset = ReadUInt16BE(reader);
                long stringsBase = nameTableOffset + stringOffset;

                // Scan name records for nameID=1 (Font Family)
                string? windowsName = null;
                string? macName = null;

                for (int i = 0; i < nameCount; i++)
                {
                    if (stream.Position + 12 > stream.Length) break;
                    ushort platformID = ReadUInt16BE(reader);
                    reader.ReadBytes(2); // encodingID
                    reader.ReadBytes(2); // languageID
                    ushort nameID = ReadUInt16BE(reader);
                    ushort length = ReadUInt16BE(reader);
                    ushort strOff = ReadUInt16BE(reader);

                    if (nameID != 1) continue;

                    long savedPos = stream.Position;
                    long targetPos = stringsBase + strOff;
                    if (targetPos + length > stream.Length) continue;

                    stream.Seek(targetPos, SeekOrigin.Begin);
                    byte[] nameBytes = reader.ReadBytes(length);
                    stream.Seek(savedPos, SeekOrigin.Begin);

                    if (platformID == 3 && windowsName == null)
                        windowsName = System.Text.Encoding.BigEndianUnicode.GetString(nameBytes);
                    else if (platformID == 1 && macName == null)
                        macName = System.Text.Encoding.ASCII.GetString(nameBytes);

                    if (windowsName != null) break; // prefer Windows name
                }

                return windowsName ?? macName;
            }
            catch
            {
                return null;
            }
        }

        private static ushort ReadUInt16BE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }

        private static uint ReadUInt32BE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
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

        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < SizeUnits.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {SizeUnits[order]}";
        }
    }

    public record ImageMetadata(uint Width, uint Height, DateTimeOffset? DateTaken,
                                 string? CameraManufacturer, string? CameraModel);

    public record MediaMetadata(TimeSpan Duration, uint Bitrate,
                                 uint? Width, uint? Height,
                                 string? Artist, string? Album);

    public record FontPreviewData
    {
        public string FilePath { get; init; } = "";
        public string FamilyName { get; init; } = "";
        public string FileName { get; init; } = "";
        public long FileSize { get; init; }
        public string Extension { get; init; } = "";
    }
}
