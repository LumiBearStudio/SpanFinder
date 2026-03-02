using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;

namespace Span.ViewModels
{
    /// <summary>
    /// 파일/폴더 공통 뷰모델 베이스 클래스. IFileSystemItem을 래핑하여
    /// 이름, 경로, 아이콘, 썸네일, 인라인 이름 변경(F2), 클라우드/Git 상태 뱃지,
    /// 경로 하이라이트 배경, Details 모드용 날짜/크기/타입 프로퍼티를 제공.
    /// </summary>
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

        /// <summary>
        /// 재귀 검색 결과에서 검색 루트 기준 상대 부모 경로.
        /// 일반 탐색에서는 빈 문자열 → UI 영향 없음.
        /// </summary>
        [ObservableProperty]
        private string _locationPath = string.Empty;

        [ObservableProperty]
        private BitmapImage? _thumbnailSource;

        /// <summary>
        /// ContainerContentChanging에서 Cloud/Git 상태 주입 완료 플래그.
        /// 스크롤 중 동일 아이템 재주입을 방지하여 PropertyChanged 폭포를 줄인다.
        /// </summary>
        internal bool CloudStateInjected;
        internal bool GitStateInjected;

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

        partial void OnCloudStateChanged(Models.CloudState oldValue, Models.CloudState value)
        {
            var newGlyph = Services.CloudSyncService.GetCloudStateGlyph(value);
            if (CloudStateGlyph != newGlyph)
                CloudStateGlyph = newGlyph;
            // HasCloudBadge/CloudBadgeBrush 알림은 실제 뱃지 표시 여부가 변경될 때만 발생
            bool wasBadge = oldValue != Models.CloudState.None;
            bool isBadge = value != Models.CloudState.None;
            if (wasBadge != isBadge)
                OnPropertyChanged(nameof(HasCloudBadge));
            if (wasBadge || isBadge) // 뱃지가 있었거나 있을 때만 브러시 알림
                OnPropertyChanged(nameof(CloudBadgeBrush));
        }

        /// <summary>
        /// 클라우드 뱃지 표시 여부.
        /// </summary>
        public bool HasCloudBadge => CloudState != Models.CloudState.None;

        /// <summary>
        /// 클라우드 상태별 배지 배경색 (정적 캐싱으로 GC 압박 방지).
        /// CloudOnly=파랑, Synced=초록, PendingUpload=주황, Syncing=파랑.
        /// </summary>
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _cloudBlueBrush
            = new(Windows.UI.Color.FromArgb(255, 0, 120, 212));    // #0078D4 Blue
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _cloudGreenBrush
            = new(Windows.UI.Color.FromArgb(255, 16, 124, 16));    // #107C10 Green
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _cloudOrangeBrush
            = new(Windows.UI.Color.FromArgb(255, 255, 140, 0));    // #FF8C00 Orange

        public Microsoft.UI.Xaml.Media.Brush CloudBadgeBrush => CloudState switch
        {
            Models.CloudState.CloudOnly => _cloudBlueBrush,
            Models.CloudState.Synced => _cloudGreenBrush,
            Models.CloudState.PendingUpload => _cloudOrangeBrush,
            Models.CloudState.Syncing => _cloudBlueBrush,
            _ => TransparentBrush,
        };

        // HasCloudBadge 알림은 OnCloudStateChanged에서 조건부로 처리
        // CloudStateGlyph 변경 시 추가 알림 불필요

        // --- Git 상태 ---

        [ObservableProperty]
        private Models.GitFileState _gitState = Models.GitFileState.None;

        partial void OnGitStateChanged(Models.GitFileState oldValue, Models.GitFileState value)
        {
            bool wasBadge = oldValue != Models.GitFileState.None && oldValue != Models.GitFileState.Clean;
            bool isBadge = value != Models.GitFileState.None && value != Models.GitFileState.Clean;
            // 뱃지 표시 여부가 변경될 때만 HasGitBadge 알림
            if (wasBadge != isBadge)
                OnPropertyChanged(nameof(HasGitBadge));
            // 뱃지가 있었거나 있을 때만 텍스트/브러시 알림
            if (wasBadge || isBadge)
            {
                OnPropertyChanged(nameof(GitStatusText));
                OnPropertyChanged(nameof(GitStatusBrush));
            }
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
        /// Git 상태별 텍스트 색상 (VS Code 스타일, 정적 캐싱으로 GC 압박 방지).
        /// </summary>
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _gitModifiedBrush
            = new(Windows.UI.Color.FromArgb(255, 226, 165, 46));     // #E2A52E 주황
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _gitAddedBrush
            = new(Windows.UI.Color.FromArgb(255, 115, 201, 145));    // #73C991 초록
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _gitDeletedBrush
            = new(Windows.UI.Color.FromArgb(255, 244, 71, 71));      // #F44747 빨강
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _gitRenamedBrush
            = new(Windows.UI.Color.FromArgb(255, 197, 134, 192));    // #C586C0 보라
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _gitConflictBrush
            = new(Windows.UI.Color.FromArgb(255, 255, 0, 0));        // #FF0000 빨강진

        public Microsoft.UI.Xaml.Media.Brush GitStatusBrush => GitState switch
        {
            Models.GitFileState.Modified => _gitModifiedBrush,
            Models.GitFileState.Added => _gitAddedBrush,
            Models.GitFileState.Deleted => _gitDeletedBrush,
            Models.GitFileState.Renamed => _gitRenamedBrush,
            Models.GitFileState.Untracked => _gitAddedBrush,
            Models.GitFileState.Conflicted => _gitConflictBrush,
            _ => TransparentBrush,
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
        /// DI 해석을 static 캐시하여 14K 아이템 로드 시 반복 해석 방지.
        /// </summary>
        private static bool? _cachedShowFileExtensions;

        /// <summary>
        /// 설정 변경 시 캐시 무효화. SettingsService에서 호출.
        /// </summary>
        internal static void InvalidateDisplayNameCache() => _cachedShowFileExtensions = null;

        public virtual string DisplayName
        {
            get
            {
                if (this is FolderViewModel) return Name;
                try
                {
                    _cachedShowFileExtensions ??= (App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService)?.ShowFileExtensions ?? true;
                    if (!_cachedShowFileExtensions.Value)
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
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush _whiteIconBrush = new(Microsoft.UI.Colors.White);
        public virtual Microsoft.UI.Xaml.Media.Brush IconBrush => _whiteIconBrush;
        public IFileSystemItem Model => _model;

        /// <summary>
        /// Rich tooltip text: Name + Type + Size + DateModified.
        /// 지연 계산으로 14K 아이템 로드 시 42K 불필요 문자열 할당 방지.
        /// </summary>
        private string? _cachedTooltip;

        public string TooltipText
        {
            get
            {
                return _cachedTooltip ??= BuildTooltipText();
            }
        }

        private string BuildTooltipText()
        {
            if (this is FolderViewModel)
                return $"{Name}\n종류: 폴더\n수정한 날짜: {DateModified}";
            if (this is FileViewModel)
                return $"{Name}\n종류: {FileType}\n크기: {Size}\n수정한 날짜: {DateModified}";
            return Name;
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
                DateTime dt = DateTime.MinValue;
                if (_model is FileItem fileItem)
                    dt = fileItem.DateModified;
                else if (_model is FolderItem folderItem)
                    dt = folderItem.DateModified;

                // MinValue 또는 비정상 날짜는 빈 문자열로 표시
                if (dt == DateTime.MinValue || dt.Year < 1980)
                    return string.Empty;

                return dt.ToString("yyyy-MM-dd HH:mm");
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

        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < SizeUnits.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {SizeUnits[order]}";
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
                    _cachedTooltip = null; // 이름 변경 후 툴팁 캐시 무효화
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(Path));
                    return true;
                }
                else
                {
                    // ── 로컬 이름 변경 (Task.Run으로 UI 프리즈 방지) ──
                    string dir = System.IO.Path.GetDirectoryName(Path)!;
                    string newPath = System.IO.Path.Combine(dir, fullNewName);
                    string currentPath = Path;
                    bool isFolder = this is FolderViewModel;

                    System.Threading.Tasks.Task.Run(() =>
                    {
                        if (isFolder)
                            System.IO.Directory.Move(currentPath, newPath);
                        else
                            System.IO.File.Move(currentPath, newPath);
                    }).GetAwaiter().GetResult();

                    _model.Name = fullNewName;
                    _model.Path = newPath;
                    _cachedTooltip = null; // 이름 변경 후 툴팁 캐시 무효화
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

        private static Microsoft.UI.Xaml.Media.Brush? _cachedPathHighlightBrush;
        internal static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);

        internal static Microsoft.UI.Xaml.Media.Brush GetPathHighlightBrush()
        {
            if (_cachedPathHighlightBrush != null) return _cachedPathHighlightBrush;
            try
            {
                if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SpanPathHighlightBrush", out var brush))
                {
                    _cachedPathHighlightBrush = (Microsoft.UI.Xaml.Media.Brush)brush;
                    return _cachedPathHighlightBrush;
                }
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FileSystemViewModel] PathHighlightBrush lookup failed: {ex.Message}"); }
            return TransparentBrush;
        }

        internal static void InvalidatePathHighlightCache() => _cachedPathHighlightBrush = null;

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
        /// XAML x:Bind: Opacity 1 when true, 0 when false.
        /// Visibility 대신 Opacity를 사용하면 레이아웃 패스 없이 렌더링만 변경되어
        /// 대용량 리스트 스크롤 시 지터를 방지한다.
        /// </summary>
        public static double OpacityIfTrue(bool value) => value ? 1.0 : 0.0;

        /// <summary>
        /// XAML x:Bind: Opacity 1 when false, 0 when true.
        /// </summary>
        public static double OpacityIfFalse(bool value) => value ? 0.0 : 1.0;

        /// <summary>
        /// 이름 변경 취소 (Esc).
        /// </summary>
        public void CancelRename()
        {
            IsRenaming = false;
        }
    }
}
