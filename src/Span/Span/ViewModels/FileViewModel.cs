using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Span.ViewModels
{
    public class FileViewModel : FileSystemViewModel
    {
        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
        };

        private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".flv", ".3gp"
        };

        /// <summary>
        /// 동시 썸네일 로딩 제한 (Shell API 과부하 방지).
        /// 전역 제한: 최대 6개 동시 썸네일 로드.
        /// </summary>
        private static readonly SemaphoreSlim _thumbnailThrottle = new(6, 6);

        private bool _thumbnailLoaded;
        private bool _thumbnailLoading;

        public FileViewModel(FileItem model) : base(model)
        {
        }

        /// <summary>
        /// 확장자 기반 아이콘 (Segoe Fluent Icons)
        /// </summary>
        public override string IconGlyph => Services.IconService.Current.GetIcon(((FileItem)_model).FileType);

        public override Microsoft.UI.Xaml.Media.Brush IconBrush => Services.IconService.Current.GetBrush(((FileItem)_model).FileType);

        private bool IsImageFile => _imageExtensions.Contains(System.IO.Path.GetExtension(Name));
        private bool IsVideoFile => _videoExtensions.Contains(System.IO.Path.GetExtension(Name));

        public override bool IsThumbnailSupported => IsImageFile || IsVideoFile;

        /// <summary>
        /// Load thumbnail asynchronously. Called when item becomes visible.
        /// Decodes to a small size to minimize memory usage.
        /// For cloud-only files (iCloud, OneDrive, etc.), uses Shell cached thumbnails
        /// to avoid triggering file downloads.
        /// </summary>
        public async Task LoadThumbnailAsync(int decodePixelWidth = 96)
        {
            if (_thumbnailLoaded || _thumbnailLoading) return;
            if (!IsThumbnailSupported) return;

            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings != null && !settings.ShowThumbnails) return;
            }
            catch { return; }

            _thumbnailLoading = true;

            // 동시 로딩 제한 (Shell API 과부하 방지)
            await _thumbnailThrottle.WaitAsync();
            try
            {
                // SemaphoreSlim 대기 중 이미 로드/취소되었을 수 있음
                if (_thumbnailLoaded) return;

                var filePath = Path;
                if (!File.Exists(filePath)) return;

                bool isCloudOnly = Services.CloudSyncService.IsCloudOnlyFile(filePath);

                // Video files & cloud-only files: use Shell thumbnail API
                // (videos can't be decoded via BitmapImage; cloud files must not trigger download)
                if (IsVideoFile || isCloudOnly)
                {
                    await LoadShellThumbnailAsync(filePath, decodePixelWidth, isCloudOnly);
                    return;
                }

                // Local image files: read directly (fastest)
                var fileInfo = new FileInfo(filePath);
                // Skip files larger than 20MB for performance
                if (fileInfo.Length > 20 * 1024 * 1024) return;

                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.DecodePixelType = DecodePixelType.Logical;

                using var stream = File.OpenRead(filePath);
                using var memStream = new MemoryStream();
                await stream.CopyToAsync(memStream);
                memStream.Position = 0;

                // Guard: column may have been removed during async I/O
                if (!_thumbnailLoading) return;

                var ras = memStream.AsRandomAccessStream();
                await bitmap.SetSourceAsync(ras);

                // Guard again after SetSourceAsync (another await point)
                if (!_thumbnailLoading) return;

                ThumbnailSource = bitmap;
                _thumbnailLoaded = true;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Thumbnail load failed for {Name}: {ex.Message}");
            }
            finally
            {
                _thumbnailThrottle.Release();
                _thumbnailLoading = false;
            }
        }

        /// <summary>
        /// Windows Shell API로 썸네일을 가져옴.
        /// 동영상: Shell이 프레임 캡처 썸네일 생성.
        /// 클라우드 전용: ReturnOnlyIfCached로 다운로드 방지, 캐시 없으면 스킵.
        /// </summary>
        private async Task LoadShellThumbnailAsync(string filePath, int decodePixelWidth, bool cacheOnly)
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                var options = cacheOnly
                    ? ThumbnailOptions.ReturnOnlyIfCached
                    : ThumbnailOptions.UseCurrentScale;

                using var thumbnail = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    (uint)decodePixelWidth,
                    options);

                if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                {
                    // Guard: column may have been removed during async I/O
                    if (!_thumbnailLoading) return;

                    var bitmap = new BitmapImage();
                    bitmap.DecodePixelWidth = decodePixelWidth;
                    bitmap.DecodePixelType = DecodePixelType.Logical;
                    await bitmap.SetSourceAsync(thumbnail);

                    if (!_thumbnailLoading) return;

                    ThumbnailSource = bitmap;
                    _thumbnailLoaded = true;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Shell thumbnail failed for {Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear loaded thumbnail to free memory.
        /// Also resets the loading flag to prevent orphaned async tasks
        /// from writing back to this ViewModel after column removal.
        /// </summary>
        public void UnloadThumbnail()
        {
            _thumbnailLoading = false;
            _thumbnailLoaded = false;
            ThumbnailSource = null;
        }
    }
}
