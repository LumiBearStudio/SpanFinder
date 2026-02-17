using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Span.ViewModels
{
    public class FileViewModel : FileSystemViewModel
    {
        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
        };

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

        public override bool IsThumbnailSupported =>
            _imageExtensions.Contains(System.IO.Path.GetExtension(Name));

        /// <summary>
        /// Load thumbnail asynchronously. Called when item becomes visible.
        /// Decodes to a small size to minimize memory usage.
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

            try
            {
                var filePath = Path;
                if (!File.Exists(filePath)) return;

                var fileInfo = new FileInfo(filePath);
                // Skip files larger than 20MB for performance
                if (fileInfo.Length > 20 * 1024 * 1024) return;

                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.DecodePixelType = DecodePixelType.Logical;

                using var stream = File.OpenRead(filePath);
                var memStream = new MemoryStream();
                await stream.CopyToAsync(memStream);
                memStream.Position = 0;

                var ras = memStream.AsRandomAccessStream();
                await bitmap.SetSourceAsync(ras);

                ThumbnailSource = bitmap;
                _thumbnailLoaded = true;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Thumbnail load failed for {Name}: {ex.Message}");
            }
            finally
            {
                _thumbnailLoading = false;
            }
        }

        /// <summary>
        /// Clear loaded thumbnail to free memory.
        /// </summary>
        public void UnloadThumbnail()
        {
            ThumbnailSource = null;
            _thumbnailLoaded = false;
        }
    }
}
