using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
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
        private bool _isOnPath;

        [ObservableProperty]
        private string _editableName = string.Empty;

        [ObservableProperty]
        private BitmapImage? _thumbnailSource;

        /// <summary>
        /// 클라우드 동기화 상태 글리프 (OneDrive 등).
        /// 빈 문자열이면 뱃지 숨김.
        /// </summary>
        [ObservableProperty]
        private string _cloudStateGlyph = string.Empty;

        /// <summary>
        /// 클라우드 동기화 상태.
        /// </summary>
        [ObservableProperty]
        private Models.CloudState _cloudState = Models.CloudState.None;

        partial void OnCloudStateChanged(Models.CloudState value)
        {
            CloudStateGlyph = Services.CloudSyncService.GetCloudStateGlyph(value);
            OnPropertyChanged(nameof(CloudBadgeBrush));
        }

        /// <summary>
        /// 클라우드 뱃지 표시 여부.
        /// </summary>
        public bool HasCloudBadge => CloudState != Models.CloudState.None;

        /// <summary>
        /// 클라우드 상태별 배지 배경색.
        /// CloudOnly=파랑, Synced=초록, PendingUpload=주황, Syncing=파랑.
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush CloudBadgeBrush => CloudState switch
        {
            Models.CloudState.CloudOnly => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0, 120, 212)),    // #0078D4 Blue
            Models.CloudState.Synced => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 16, 124, 16)),    // #107C10 Green
            Models.CloudState.PendingUpload => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 255, 140, 0)),    // #FF8C00 Orange
            Models.CloudState.Syncing => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0, 120, 212)),    // #0078D4 Blue
            _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        partial void OnCloudStateGlyphChanged(string value)
        {
            OnPropertyChanged(nameof(HasCloudBadge));
        }

        // --- Git 상태 ---

        [ObservableProperty]
        private Models.GitFileState _gitState = Models.GitFileState.None;

        partial void OnGitStateChanged(Models.GitFileState value)
        {
            OnPropertyChanged(nameof(GitStatusText));
            OnPropertyChanged(nameof(GitStatusBrush));
            OnPropertyChanged(nameof(HasGitBadge));
        }

        /// <summary>
        /// Git 뱃지 표시 여부 (Modified/Added/Deleted/Renamed/Untracked/Conflicted).
        /// None과 Clean은 뱃지를 표시하지 않음.
        /// </summary>
        public bool HasGitBadge => GitState != Models.GitFileState.None
                                && GitState != Models.GitFileState.Clean;

        /// <summary>
        /// Git 상태 텍스트 (M/A/D/R/?/!).
        /// </summary>
        public string GitStatusText => GitState switch
        {
            Models.GitFileState.Modified => "M",
            Models.GitFileState.Added => "A",
            Models.GitFileState.Deleted => "D",
            Models.GitFileState.Renamed => "R",
            Models.GitFileState.Untracked => "?",
            Models.GitFileState.Conflicted => "!",
            _ => "",
        };

        /// <summary>
        /// Git 상태별 텍스트 색상 (VS Code 스타일).
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush GitStatusBrush => GitState switch
        {
            Models.GitFileState.Modified => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 226, 165, 46)),     // #E2A52E 주황
            Models.GitFileState.Added => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 115, 201, 145)),    // #73C991 초록
            Models.GitFileState.Deleted => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 244, 71, 71)),      // #F44747 빨강
            Models.GitFileState.Renamed => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 197, 134, 192)),    // #C586C0 보라
            Models.GitFileState.Untracked => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 115, 201, 145)),    // #73C991 초록
            Models.GitFileState.Conflicted => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 255, 0, 0)),        // #FF0000 빨강진
            _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        public string Name => _model.Name;
        public string Path => _model.Path;

        /// <summary>
        /// 숨김 파일/폴더 반투명 표시를 위한 불투명도.
        /// Hidden=0.5, Normal=1.0
        /// </summary>
        public double ItemOpacity => _model.IsHidden ? 0.5 : 1.0;

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

            try
            {
                if (Services.FileSystemRouter.IsRemotePath(Path))
                {
                    // ── 원격 이름 변경 ──
                    var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
                    var provider = router.GetConnectionForPath(Path);
                    if (provider == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Rename error: 원격 연결을 찾을 수 없습니다");
                        return false;
                    }

                    var remotePath = Services.FileSystemRouter.ExtractRemotePath(Path);
                    var parentDir = remotePath.Contains('/')
                        ? remotePath[..remotePath.TrimEnd('/').LastIndexOf('/')]
                        : "/";
                    if (string.IsNullOrEmpty(parentDir)) parentDir = "/";
                    var newRemotePath = parentDir.TrimEnd('/') + "/" + fullNewName;

                    provider.RenameAsync(remotePath, newRemotePath).GetAwaiter().GetResult();

                    // URI prefix 보존하여 전체 경로 재구성
                    var uriPrefix = Path[..(Path.Length - remotePath.Length)];
                    var newPath = uriPrefix + newRemotePath;

                    _model.Name = fullNewName;
                    _model.Path = newPath;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(Path));
                    return true;
                }
                else
                {
                    // ── 로컬 이름 변경 ──
                    string dir = System.IO.Path.GetDirectoryName(Path)!;
                    string newPath = System.IO.Path.Combine(dir, fullNewName);

                    if (this is FolderViewModel)
                        System.IO.Directory.Move(Path, newPath);
                    else
                        System.IO.File.Move(Path, newPath);

                    _model.Name = fullNewName;
                    _model.Path = newPath;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(Path));
                    return true;
                }
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

        /// <summary>
        /// Brush for path highlight background, set directly by UpdatePathHighlights().
        /// Using [ObservableProperty] for reliable PropertyChanged notification.
        /// </summary>
        [ObservableProperty]
        private Microsoft.UI.Xaml.Media.Brush _pathBackground = TransparentBrush;

        private static Microsoft.UI.Xaml.Media.Brush? _cachedAccentDimBrush;
        internal static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);

        internal static Microsoft.UI.Xaml.Media.Brush GetAccentDimBrush()
        {
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
            return TransparentBrush;
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
