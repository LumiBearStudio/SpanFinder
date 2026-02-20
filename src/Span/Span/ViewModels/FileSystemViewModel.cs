using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;

namespace Span.ViewModels
{
    public partial class FileSystemViewModel : ObservableObject
    {
        protected readonly IFileSystemItem _model;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isRenaming;

        /// <summary>
        /// True when this item is on the active navigation path (selected in a parent column).
        /// Used to show a dimmed highlight in non-active columns for breadcrumb trail visualization.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PathBackground))]
        private bool _isOnPath;

        [ObservableProperty]
        private string _editableName = string.Empty;

        [ObservableProperty]
        private BitmapImage? _thumbnailSource;

        public string Name => _model.Name;
        public string Path => _model.Path;

        /// <summary>
        /// Display name that respects ShowFileExtensions setting.
        /// Folders always show full name; files strip extension when setting is off.
        /// </summary>
        public virtual string DisplayName
        {
            get
            {
                if (this is FolderViewModel) return Name;
                try
                {
                    var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                    if (settings != null && !settings.ShowFileExtensions)
                        return System.IO.Path.GetFileNameWithoutExtension(Name);
                }
                catch { /* fallback to full name */ }
                return Name;
            }
        }

        /// <summary>
        /// Display name with middle truncation for files.
        /// For files with extensions longer than MaxTruncateChars: "VeryLong...name.txt"
        /// For folders or short names: returns DisplayName as-is.
        /// </summary>
        public virtual string TruncatedDisplayName
        {
            get
            {
                var name = DisplayName;

                // Only truncate file names (not folders)
                if (this is FolderViewModel)
                    return name;

                return MiddleTruncate(name, 28);
            }
        }

        /// <summary>
        /// Middle-truncates a filename preserving extension.
        /// "VeryLongFileName.txt" -> "VeryLo...ame.txt"
        /// </summary>
        private static string MiddleTruncate(string name, int maxChars)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= maxChars)
                return name;

            var ext = System.IO.Path.GetExtension(name);
            var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(name);

            // If no extension, use simple end truncation
            if (string.IsNullOrEmpty(ext))
                return name;

            // Keep at least 3 chars of the ellipsis
            const string ellipsis = "\u2026"; // Unicode horizontal ellipsis
            int extLen = ext.Length;

            // Reserve space for extension + ellipsis + at least a few chars on each side
            int availableChars = maxChars - extLen - 1; // -1 for ellipsis character
            if (availableChars < 6)
                return name; // Too short to truncate meaningfully

            // Split available chars: more at the beginning, some at the end (before extension)
            int prefixLen = (int)(availableChars * 0.6);
            int suffixLen = availableChars - prefixLen;

            if (prefixLen >= nameWithoutExt.Length)
                return name; // No truncation needed

            string prefix = nameWithoutExt.Substring(0, prefixLen);
            string suffix = nameWithoutExt.Substring(nameWithoutExt.Length - suffixLen);

            return prefix + ellipsis + suffix + ext;
        }

        public virtual string IconGlyph => _model.IconGlyph;
        public virtual Microsoft.UI.Xaml.Media.Brush IconBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        public IFileSystemItem Model => _model;

        /// <summary>
        /// Rich tooltip text: Name + Type + Size + DateModified.
        /// </summary>
        public string TooltipText
        {
            get
            {
                if (this is FolderViewModel)
                    return $"{Name}\n종류: 폴더\n수정한 날짜: {DateModified}";
                if (this is FileViewModel)
                    return $"{Name}\n종류: {FileType}\n크기: {Size}\n수정한 날짜: {DateModified}";
                return Name;
            }
        }

        /// <summary>
        /// Whether this item has a thumbnail loaded. Used by XAML to toggle Image vs FontIcon.
        /// </summary>
        public bool HasThumbnail => ThumbnailSource != null;

        /// <summary>
        /// Whether this item supports thumbnail preview (image files only).
        /// </summary>
        public virtual bool IsThumbnailSupported => false;

        partial void OnThumbnailSourceChanged(BitmapImage? value)
        {
            OnPropertyChanged(nameof(HasThumbnail));
        }

        // Properties for Details mode
        public virtual string DateModified
        {
            get
            {
                if (_model is FileItem fileItem)
                    return fileItem.DateModified.ToString("yyyy-MM-dd HH:mm");
                if (_model is FolderItem folderItem)
                    return folderItem.DateModified.ToString("yyyy-MM-dd HH:mm");
                return string.Empty;
            }
        }

        public virtual System.DateTime DateModifiedValue
        {
            get
            {
                if (_model is FileItem fileItem)
                    return fileItem.DateModified;
                if (_model is FolderItem folderItem)
                    return folderItem.DateModified;
                return System.DateTime.MinValue;
            }
        }

        public virtual string FileType
        {
            get
            {
                if (_model is FileItem fileItem)
                    return string.IsNullOrEmpty(fileItem.FileType) ? "File" : fileItem.FileType.TrimStart('.');
                return "Folder";
            }
        }

        public virtual string Size
        {
            get
            {
                if (_model is FileItem fileItem)
                    return FormatFileSize(fileItem.Size);
                return string.Empty; // Folders don't show size in Details mode
            }
        }

        public virtual long SizeValue
        {
            get
            {
                if (_model is FileItem fileItem)
                    return fileItem.Size;
                return 0;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        public FileSystemViewModel(IFileSystemItem model)
        {
            _model = model;
        }

        /// <summary>
        /// F2 인라인 이름 변경 시작.
        /// Windows Explorer 방식: 파일도 확장자 포함한 전체 이름 표시 (선택 영역만 파일명).
        /// </summary>
        public void BeginRename()
        {
            // 폴더, 파일 모두 전체 이름 사용 (Windows Explorer 동작)
            EditableName = Name;
            IsRenaming = true;
        }

        /// <summary>
        /// 이름 변경 커밋 (Enter).
        /// </summary>
        public bool CommitRename()
        {
            IsRenaming = false;
            string newName = EditableName.Trim();

            // Windows Explorer 방식: 전체 이름(확장자 포함) 그대로 사용
            if (string.IsNullOrEmpty(newName) || newName == Name)
                return false;
            string fullNewName = newName;

            string dir = System.IO.Path.GetDirectoryName(Path)!;
            string newPath = System.IO.Path.Combine(dir, fullNewName);

            try
            {
                if (this is FolderViewModel)
                    System.IO.Directory.Move(Path, newPath);
                else
                    System.IO.File.Move(Path, newPath);

                // 모델 업데이트
                _model.Name = fullNewName;
                _model.Path = newPath;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Path));
                return true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Rename error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// XAML x:Bind 함수 바인딩용 (IsRenaming의 반전).
        /// </summary>
        public static Microsoft.UI.Xaml.Visibility NotRenaming(bool isRenaming)
            => isRenaming ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        private static Microsoft.UI.Xaml.Media.Brush? _cachedAccentDimBrush;
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _transparentBrush = new(Microsoft.UI.Colors.Transparent);

        /// <summary>
        /// Direct property for path highlight background.
        /// Returns SpanAccentDimBrush for on-path items, Transparent otherwise.
        /// Bound directly in XAML (more reliable than x:Bind function parameter tracking).
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush PathBackground
        {
            get
            {
                if (!IsOnPath) return _transparentBrush;
                if (_cachedAccentDimBrush != null) return _cachedAccentDimBrush;
                try
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SpanAccentDimBrush", out var brush))
                    {
                        _cachedAccentDimBrush = (Microsoft.UI.Xaml.Media.Brush)brush;
                        return _cachedAccentDimBrush;
                    }
                }
                catch { }
                return _transparentBrush;
            }
        }

        /// <summary>
        /// XAML x:Bind: show thumbnail Image when HasThumbnail is true.
        /// </summary>
        public static Microsoft.UI.Xaml.Visibility ShowIfTrue(bool value)
            => value ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        /// <summary>
        /// XAML x:Bind: show FontIcon when HasThumbnail is false.
        /// </summary>
        public static Microsoft.UI.Xaml.Visibility ShowIfFalse(bool value)
            => value ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        /// <summary>
        /// 이름 변경 취소 (Esc).
        /// </summary>
        public void CancelRename()
        {
            IsRenaming = false;
        }
    }
}
