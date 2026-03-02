using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Span.Models;
using Span.Services;

namespace Span.ViewModels
{
    /// <summary>
    /// 탐색기 뷰모델. Miller Columns, Details, Icon 뷰 공통의 폴더 탐색 엔진.
    /// 컬럼 계층(Columns), 브레드크럼(PathSegments), Back/Forward 내비게이션 히스토리를 관리.
    /// 선택 디바운싱(150ms), 경로 하이라이트, 원격(FTP/SFTP/SMB) 경로 탐색을 지원.
    /// </summary>
    public partial class ExplorerViewModel : ObservableObject
    {
        // Columns for Miller View
        public ObservableCollection<FolderViewModel> Columns { get; }

        // 브레드크럼 세그먼트 (주소 표시줄)
        public ObservableCollection<PathSegment> PathSegments { get; } = new();

        // Current active path (for address bar)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentFolderName))]
        private string _currentPath = string.Empty;

        public string CurrentFolderName => System.IO.Path.GetFileName(CurrentPath) is string s && !string.IsNullOrEmpty(s) ? s : CurrentPath;

        /// <summary>
        /// The currently selected file (when a file is selected in the last column).
        /// Used to drive the inline preview column in Miller Columns mode.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPreviewColumn))]
        private FileViewModel? _selectedFile;

        /// <summary>
        /// True when a file is selected in the last column and the inline preview column should be shown.
        /// </summary>
        public bool ShowPreviewColumn => _selectedFile != null;

        /// <summary>
        /// 현재 활성 폴더 (Details/Icon 모드용)
        /// Miller Columns의 마지막 컬럼 반환
        /// </summary>
        public FolderViewModel? CurrentFolder => Columns.LastOrDefault();

        /// <summary>
        /// 현재 표시할 항목 리스트 (Details/Icon 모드용)
        /// </summary>
        public ObservableCollection<FileSystemViewModel> CurrentItems =>
            CurrentFolder?.Children ?? new ObservableCollection<FileSystemViewModel>();

        /// <summary>
        /// 필터 바 텍스트. 설정 시 모든 컬럼에 ApplyFilter 전파.
        /// </summary>
        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value ?? string.Empty))
                {
                    // 필터 적용 중 Children 교체 → SelectedChild 변경 → Columns 수정 연쇄를 방지
                    // 1) Columns 스냅샷으로 순회 (ConcurrentModificationException 방지)
                    // 2) AutoNavigation 억제 (필터로 인한 컬럼 추가/제거 방지)
                    var prevAutoNav = EnableAutoNavigation;
                    EnableAutoNavigation = false;
                    try
                    {
                        foreach (var col in Columns.ToList())
                            col.ApplyFilter(_filterText);
                    }
                    finally
                    {
                        EnableAutoNavigation = prevAutoNav;
                    }
                    OnPropertyChanged(nameof(IsFilterActive));
                }
            }
        }

        /// <summary>
        /// 필터가 활성화되어 있는지 여부.
        /// </summary>
        public bool IsFilterActive => !string.IsNullOrEmpty(_filterText);

        private readonly FileSystemService _fileService;

        // Debouncing for folder selection (Phase 1)
        private CancellationTokenSource? _selectionDebounce;
        private const int SelectionDebounceMs = 150;

        // Suppresses CollectionChanged → PropertyChanged during Cleanup to prevent
        // notifications reaching already-disposed UI elements (causes win32 crash)
        private bool _isCleaningUp = false;

        /// <summary>
        /// Controls automatic navigation on selection change.
        /// TRUE: Miller Columns mode - navigate on single click
        /// FALSE: Details/Icon mode - selection only, navigate on double click
        /// </summary>
        public bool EnableAutoNavigation { get; set; } = true;

        /// <summary>
        /// 폴더 로딩 또는 경로 탐색 실패 시 에러 메시지를 전파하는 이벤트.
        /// MainWindow에서 구독하여 토스트 알림으로 표시.
        /// </summary>
        public event Action<string>? NavigationError;

        // ── Back/Forward Navigation History ──
        private const int MaxHistorySize = 50;
        private readonly Stack<string> _backStack = new();
        private readonly Stack<string> _forwardStack = new();
        private bool _isNavigatingHistory = false;

        [ObservableProperty]
        private bool _canGoBack = false;

        [ObservableProperty]
        private bool _canGoForward = false;

        public ExplorerViewModel(FolderItem rootItem, FileSystemService fileService)
        {
            Columns = new ObservableCollection<FolderViewModel>();

            // CRITICAL: Notify UI when Columns changes so CurrentFolder/CurrentItems update
            // Guard with _isCleaningUp to prevent PropertyChanged reaching disposed UI during shutdown
            Columns.CollectionChanged += (s, e) =>
            {
                if (_isCleaningUp) return;
                OnPropertyChanged(nameof(CurrentFolder));
                OnPropertyChanged(nameof(CurrentItems));
            };

            _fileService = fileService;
        }

        /// <summary>
        /// CurrentPath 변경 시 PathSegments를 자동 갱신.
        /// </summary>
        partial void OnCurrentPathChanged(string value)
        {
            UpdatePathSegments(value);
            UpdatePathHighlights();
        }

        // ── Back/Forward Navigation History Methods ──

        /// <summary>
        /// Push the current path to the back stack before navigating to a new path.
        /// Clears the forward stack (standard browser/explorer behavior).
        /// Called by navigation methods BEFORE changing CurrentPath.
        /// </summary>
        private void PushToHistory(string newPath)
        {
            // Don't push during GoBack/GoForward operations
            if (_isNavigatingHistory) return;

            var current = CurrentPath;

            // Don't push empty/null/identical paths
            if (string.IsNullOrEmpty(current)) return;
            if (string.Equals(current, newPath, System.StringComparison.OrdinalIgnoreCase)) return;

            // Push current to back stack
            _backStack.Push(current);

            // Trim to max size
            if (_backStack.Count > MaxHistorySize)
            {
                var temp = _backStack.ToArray();
                _backStack.Clear();
                for (int i = 0; i < MaxHistorySize; i++)
                    _backStack.Push(temp[MaxHistorySize - 1 - i]);
            }

            // Clear forward stack on normal navigation
            _forwardStack.Clear();

            UpdateHistoryState();
        }

        /// <summary>
        /// Navigate to the previous path in the back stack.
        /// </summary>
        public async Task GoBack()
        {
            if (_backStack.Count == 0) return;

            var previousPath = _backStack.Pop();

            // Push current path to forward stack
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                _forwardStack.Push(CurrentPath);
            }

            UpdateHistoryState();

            // Navigate without affecting history stacks
            _isNavigatingHistory = true;
            try
            {
                if (FileSystemRouter.IsRemotePath(previousPath) || System.IO.Directory.Exists(previousPath))
                {
                    await NavigateToPath(previousPath);
                }
                else
                {
                    Helpers.DebugLogger.Log($"[GoBack] Path no longer exists: {previousPath}");
                    // Try the next entry
                    _isNavigatingHistory = false;
                    await GoBack();
                    return;
                }
            }
            finally
            {
                _isNavigatingHistory = false;
            }

            Helpers.DebugLogger.Log($"[GoBack] Navigated to: {previousPath} (back={_backStack.Count}, forward={_forwardStack.Count})");
        }

        /// <summary>
        /// Navigate to the next path in the forward stack.
        /// </summary>
        public async Task GoForward()
        {
            if (_forwardStack.Count == 0) return;

            var nextPath = _forwardStack.Pop();

            // Push current path to back stack
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                _backStack.Push(CurrentPath);
            }

            UpdateHistoryState();

            // Navigate without affecting history stacks
            _isNavigatingHistory = true;
            try
            {
                if (FileSystemRouter.IsRemotePath(nextPath) || System.IO.Directory.Exists(nextPath))
                {
                    await NavigateToPath(nextPath);
                }
                else
                {
                    Helpers.DebugLogger.Log($"[GoForward] Path no longer exists: {nextPath}");
                    // Try the next entry
                    _isNavigatingHistory = false;
                    await GoForward();
                    return;
                }
            }
            finally
            {
                _isNavigatingHistory = false;
            }

            Helpers.DebugLogger.Log($"[GoForward] Navigated to: {nextPath} (back={_backStack.Count}, forward={_forwardStack.Count})");
        }

        /// <summary>
        /// Update CanGoBack/CanGoForward properties from stack state.
        /// </summary>
        private void UpdateHistoryState()
        {
            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;
        }

        /// <summary>
        /// Returns the back history as a list (most recent first).
        /// Used by the Back button dropdown menu.
        /// </summary>
        public List<string> GetBackHistory()
        {
            return _backStack.ToList();
        }

        /// <summary>
        /// Returns the forward history as a list (most recent first).
        /// Used by the Forward button dropdown menu.
        /// </summary>
        public List<string> GetForwardHistory()
        {
            return _forwardStack.ToList();
        }

        /// <summary>
        /// Navigate to a specific entry in the back history.
        /// Pops entries up to and including the target, pushes current + intermediate to forward.
        /// </summary>
        public async Task NavigateToBackHistoryEntry(int index)
        {
            if (index < 0 || index >= _backStack.Count) return;

            // Push current path to forward stack
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                _forwardStack.Push(CurrentPath);
            }

            // Pop entries from back stack; entries before the target go to forward stack
            string targetPath = string.Empty;
            for (int i = 0; i <= index; i++)
            {
                var path = _backStack.Pop();
                if (i == index)
                {
                    targetPath = path;
                }
                else
                {
                    // Intermediate entries go to forward stack
                    _forwardStack.Push(path);
                }
            }

            UpdateHistoryState();

            _isNavigatingHistory = true;
            try
            {
                if (FileSystemRouter.IsRemotePath(targetPath) || System.IO.Directory.Exists(targetPath))
                {
                    await NavigateToPath(targetPath);
                }
            }
            finally
            {
                _isNavigatingHistory = false;
            }

            Helpers.DebugLogger.Log($"[NavigateToBackHistoryEntry] Navigated to index {index}: {targetPath}");
        }

        /// <summary>
        /// Navigate to a specific entry in the forward history.
        /// Pops entries up to and including the target, pushes current + intermediate to back.
        /// </summary>
        public async Task NavigateToForwardHistoryEntry(int index)
        {
            if (index < 0 || index >= _forwardStack.Count) return;

            // Push current path to back stack
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                _backStack.Push(CurrentPath);
            }

            // Pop entries from forward stack; entries before the target go to back stack
            string targetPath = string.Empty;
            for (int i = 0; i <= index; i++)
            {
                var path = _forwardStack.Pop();
                if (i == index)
                {
                    targetPath = path;
                }
                else
                {
                    // Intermediate entries go to back stack
                    _backStack.Push(path);
                }
            }

            UpdateHistoryState();

            _isNavigatingHistory = true;
            try
            {
                if (FileSystemRouter.IsRemotePath(targetPath) || System.IO.Directory.Exists(targetPath))
                {
                    await NavigateToPath(targetPath);
                }
            }
            finally
            {
                _isNavigatingHistory = false;
            }

            Helpers.DebugLogger.Log($"[NavigateToForwardHistoryEntry] Navigated to index {index}: {targetPath}");
        }

        private void UpdatePathSegments(string path)
        {
            PathSegments.Clear();
            if (string.IsNullOrWhiteSpace(path)) return;

            // 원격 URI 경로: ftp://user@host:21/upload/docs → [host:21] > [upload] > [docs]
            if (FileSystemRouter.IsRemotePath(path) && System.Uri.TryCreate(path, System.UriKind.Absolute, out var remoteUri))
            {
                var prefix = FileSystemRouter.GetUriPrefix(path);
                // 루트 세그먼트: "host:port"
                PathSegments.Add(new PathSegment(
                    $"{remoteUri.Host}:{remoteUri.Port}",
                    prefix + "/",
                    false));

                // 하위 경로 세그먼트
                var segments = remoteUri.AbsolutePath.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
                var cumulative = prefix;
                for (int i = 0; i < segments.Length; i++)
                {
                    cumulative += "/" + segments[i];
                    PathSegments.Add(new PathSegment(
                        segments[i],
                        cumulative,
                        i == segments.Length - 1));
                }
                return;
            }

            // UNC path: \\server\share\folder\...
            if (path.StartsWith(@"\\"))
            {
                // Split by backslash, remove empties → ["server", "share", "folder", ...]
                var parts = path.TrimStart('\\').Split(
                    System.IO.Path.DirectorySeparatorChar,
                    System.StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2) return; // Need at least server + share for valid UNC

                // First segment: \\server\share (UNC root)
                string uncRoot = @"\\" + parts[0] + @"\" + parts[1];
                PathSegments.Add(new PathSegment(
                    @"\\" + parts[0] + @"\" + parts[1],
                    uncRoot,
                    isLast: parts.Length == 2));

                // Remaining segments: folders after the share
                string accumulated = uncRoot;
                for (int i = 2; i < parts.Length; i++)
                {
                    accumulated = System.IO.Path.Combine(accumulated, parts[i]);
                    PathSegments.Add(new PathSegment(parts[i], accumulated, isLast: i == parts.Length - 1));
                }
            }
            else
            {
                // Local path: C:\folder\...
                var parts = path.Split(System.IO.Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);
                string accumulated = string.Empty;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (i == 0 && parts[i].EndsWith(":"))
                    {
                        accumulated = parts[i] + "\\";
                    }
                    else
                    {
                        accumulated = System.IO.Path.Combine(accumulated, parts[i]);
                    }

                    PathSegments.Add(new PathSegment(parts[i], accumulated, isLast: i == parts.Length - 1));
                }
            }
        }

        /// <summary>
        /// Navigate to a folder from sidebar (reset all columns).
        /// </summary>
        public async Task NavigateTo(FolderItem folder)
        {
            Helpers.DebugLogger.Log($"[NavigateTo] Navigating to: {folder.Name}, clearing {Columns.Count} columns");

            // Push current path to history before navigating
            PushToHistory(folder.Path);

            // 경량 정리 — Children 유지 (캐시 효과), 구독만 해제
            foreach (var col in Columns)
            {
                col.PropertyChanged -= FolderVm_PropertyChanged;
                col.LoadError -= OnColumnLoadError;
                col.CancelLoading();
                col.SelectedChild = null;
            }
            Columns.Clear();

            var rootVm = new FolderViewModel(folder, _fileService);
            AddColumn(rootVm);                          // 즉시 UI에 추가 → ProgressRing 표시
            CurrentPath = rootVm.Path;
            SelectedFile = null;
            await rootVm.EnsureChildrenLoadedAsync();   // 로딩 완료 시 항목 표시

            Helpers.DebugLogger.Log($"[NavigateTo] Navigation complete. Current path: {CurrentPath}");
        }

        /// <summary>
        /// 문자열 경로로 직접 탐색 (주소 표시줄 편집, 브레드크럼 클릭, 세션 복원).
        /// 루트 드라이브부터 대상 폴더까지 전체 계층을 Miller Columns로 구성.
        /// 예: D:\foo\bar → [D:\] > [foo] > [bar] 세 개의 컬럼 표시.
        /// </summary>
        public async Task NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // \\?\ 접두사 제거 (.NET 8은 long path를 네이티브 지원)
            if (path.StartsWith(@"\\?\"))
                path = path.Substring(4);

            // 원격 경로: Directory.Exists 스킵, URI 그대로 사용
            if (FileSystemRouter.IsRemotePath(path))
            {
                await NavigateToRemotePath(path);
                return;
            }

            // UNC 경로: Directory.Exists가 30초+ 블로킹하므로 비동기 처리
            // 로컬 경로: 즉시 확인 (긴 경로는 비동기 처리)
            if (path.StartsWith(@"\\"))
            {
                var exists = await Task.Run(() => System.IO.Directory.Exists(path));
                if (!exists)
                {
                    NavigationError?.Invoke($"네트워크 경로에 접근할 수 없습니다: {path}");
                    return;
                }
            }
            else
            {
                var exists = path.Length > 240
                    ? await Task.Run(() => System.IO.Directory.Exists(path))
                    : System.IO.Directory.Exists(path);
                if (!exists)
                {
                    NavigationError?.Invoke($"폴더를 찾을 수 없습니다: {System.IO.Path.GetFileName(path)}");
                    return;
                }
            }

            // Normalize path (guard against PathTooLongException)
            try { path = System.IO.Path.GetFullPath(path); }
            catch (System.IO.PathTooLongException)
            {
                NavigationError?.Invoke("경로가 너무 깁니다 (260자 초과)");
                return;
            }

            // Push current path to history before navigating
            PushToHistory(path);

            // Get root and relative parts
            var root = System.IO.Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return;

            var relative = path.Substring(root.Length);
            var parts = string.IsNullOrEmpty(relative)
                ? System.Array.Empty<string>()
                : relative.Split(System.IO.Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);

            // If only root drive, use simple NavigateTo
            // Use _isNavigatingHistory to prevent NavigateTo from pushing duplicate history
            if (parts.Length == 0)
            {
                var folderItem = new FolderItem { Name = root.TrimEnd('\\'), Path = root };
                var wasNavigating = _isNavigatingHistory;
                _isNavigatingHistory = true;
                try
                {
                    await NavigateTo(folderItem);
                }
                finally
                {
                    _isNavigatingHistory = wasNavigating;
                }
                return;
            }

            // 깊은 경로 최적화: 세그먼트가 MaxMillerColumns를 초과하면
            // 마지막 N개 레벨만 Miller 컬럼으로 구축 (35단계 → 8컬럼, ~30초 → ~2초)
            const int MaxMillerColumns = 8;
            int startIndex = 0;
            string startPath = root;

            if (parts.Length > MaxMillerColumns)
            {
                startIndex = parts.Length - MaxMillerColumns;
                // 스킵된 세그먼트까지의 경로를 조합
                for (int i = 0; i < startIndex; i++)
                    startPath = System.IO.Path.Combine(startPath, parts[i]);

                Helpers.DebugLogger.Log($"[NavigateToPath] Deep path optimization: {parts.Length + 1} levels → showing last {MaxMillerColumns + 1} columns from '{startPath}'");
            }
            else
            {
                Helpers.DebugLogger.Log($"[NavigateToPath] Building full hierarchy for: {path} ({parts.Length + 1} levels)");
            }

            // Suppress auto-navigation while building column hierarchy
            var previousAutoNav = EnableAutoNavigation;
            EnableAutoNavigation = false;

            try
            {
                // 경량 정리 — Children 유지, 구독만 해제
                foreach (var col in Columns)
                {
                    col.PropertyChanged -= FolderVm_PropertyChanged;
                    col.LoadError -= OnColumnLoadError;
                    col.CancelLoading();
                    col.SelectedChild = null;
                }
                Columns.Clear();

                // ── Phase 1: 경로에서 직접 FolderViewModel 배열 생성 (I/O 없음) ──
                var startName = startIndex > 0 ? parts[startIndex - 1] : root.TrimEnd('\\');
                int columnCount = parts.Length - startIndex + 1;
                var columnVms = new FolderViewModel[columnCount];

                columnVms[0] = new FolderViewModel(
                    new FolderItem { Name = startName, Path = startPath }, _fileService);

                string accumulatedPath = startPath;
                for (int i = startIndex; i < parts.Length; i++)
                {
                    accumulatedPath = System.IO.Path.Combine(accumulatedPath, parts[i]);
                    columnVms[i - startIndex + 1] = new FolderViewModel(
                        new FolderItem { Name = parts[i], Path = accumulatedPath }, _fileService);
                }

                // ── Phase 2: 모든 컬럼을 즉시 UI에 추가 (ProgressRing 즉시 표시) ──
                for (int i = 0; i < columnVms.Length; i++)
                {
                    if (i > 0)
                        columnVms[i - 1].SelectedChild = columnVms[i];
                    AddColumn(columnVms[i]);
                }

                CurrentPath = columnVms[columnVms.Length - 1].Path;
                SelectedFile = null;

                // ── Phase 3: 모든 폴더 내용을 병렬 로딩 (UI 행 해소) ──
                await Task.WhenAll(columnVms.Select(vm => vm.EnsureChildrenLoadedAsync()));

                Helpers.DebugLogger.Log($"[NavigateToPath] Hierarchy built (parallel): {string.Join(" > ", Columns.Select(c => c.Name))}");
            }
            finally
            {
                EnableAutoNavigation = previousAutoNav;
            }
        }

        private async Task NavigateToRemotePath(string uriPath)
        {
            // Push current path to history before navigating
            PushToHistory(uriPath);

            var folder = new FolderItem
            {
                Name = System.Uri.TryCreate(uriPath, System.UriKind.Absolute, out var uri)
                    ? uri.AbsolutePath.Split('/').LastOrDefault(s => s.Length > 0) ?? uri.Host
                    : uriPath,
                Path = uriPath
            };

            // Use _isNavigatingHistory to prevent NavigateTo from pushing duplicate history
            var wasNavigating = _isNavigatingHistory;
            _isNavigatingHistory = true;
            try
            {
                await NavigateTo(folder);
            }
            finally
            {
                _isNavigatingHistory = wasNavigating;
            }
        }

        /// <summary>
        /// 브레드크럼 세그먼트 클릭 시 해당 경로까지 탐색.
        /// Finder 스타일: 이미 열려있는 컬럼 내의 경로라면 컬럼을 유지하고 하위만 정리.
        /// </summary>
        public void NavigateToSegment(PathSegment segment)
        {
            if (segment == null) return;

            // 1. 현재 컬럼들 중에서 해당 경로와 일치하는 폴더가 있는지 확인
            //    (마지막 컬럼은 선택된 '파일'이 뷰모델일 수도 있으므로, 폴더인 것들만 비교)
            int index = -1;
            for (int i = 0; i < Columns.Count; i++)
            {
                // 대소문자 무시하고 경로 비교
                if (string.Equals(Columns[i].Path, segment.FullPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                // 2. 일치하는 컬럼이 발견되면, 그 이후의 컬럼들을 모두 제거 (Truncate)
                //    여기서는 "해당 폴더를 선택한 상태"가 되어야 함.
                //    ExplorerViewModel 로직상, RemoveColumnsFrom(index + 1)를 하면
                //    Columns[0..index]는 남고, 그 뒤가 사라짐.
                //    그리고 CurrentPath를 갱신.

                // 만약 현재 마지막 컬럼(이미 선택된 끝점)과 같다면 아무것도 안 해도 됨 (단, CurrentPath는 보장)
                if (index == Columns.Count - 1 && CurrentPath.Equals(segment.FullPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Push current path to history for column-truncation navigation
                PushToHistory(segment.FullPath);

                RemoveColumnsFrom(index + 1);

                // 선택된 폴더의 SelectedChild를 null로 초기화 해야 하위가 안보임?
                // 아니, NavigateTo 로직상 보통 부모에서 얘를 선택한 상태여야 하는데...
                // 브레드크럼 클릭은 "그 폴더로 이동" 이므로, 그 폴더의 내용을 보여주는게 목적이 아니라
                // 그 폴더가 "선택된 상태" (= 그 폴더의 내용이 다음 컬럼에 나와야 함?)
                // 아니면 "그 폴더가 루트/현재위치"가 되는 것?

                // Finder 동작: A > B > C 클릭 시:
                // A, B, C 컬럼이 보이고, C가 'Active' 상태. C의 내용물은 다음 컬럼(아직 선택안함)에 표시될 준비.
                // 즉 C로 이동.

                CurrentPath = segment.FullPath;
                SelectedFile = null;

                // UI 갱신을 위해 PropertyChanged 알림이 필요할 수 있음.
                // RemoveColumnsFrom 내부에서 CollectionChanged가 발생하므로 UI는 줄어듦.
            }
            else
            {
                // 3. 컬럼에 없다면 (완전히 다른 경로로 점프하는 경우) 기존 방식대로 전체 이동
                _ = NavigateToPath(segment.FullPath);
            }
        }

        public void SetActiveColumn(FolderViewModel activeVm)
        {
            foreach (var col in Columns)
            {
                col.IsActive = (col == activeVm);
            }
        }

        /// <summary>
        /// Manually navigate into a folder (called from double-click in Details/Icon views).
        /// Bypasses EnableAutoNavigation check.
        /// When fromColumn is provided, uses it as the parent column (for Miller Columns double-click).
        /// Otherwise falls back to CurrentFolder (for Details/Icon views).
        /// </summary>
        public async void NavigateIntoFolder(FolderViewModel folder, FolderViewModel? fromColumn = null)
        {
            if (folder == null) return;

            try
            {
                Helpers.DebugLogger.Log($"[NavigateIntoFolder] Manual navigation to: {folder.Name}");

                // Push current path to history before navigating
                PushToHistory(folder.Path);

                // Find parent column index
                var parentFolder = fromColumn ?? CurrentFolder;
                if (parentFolder == null) return;

                int parentIndex = Columns.IndexOf(parentFolder);
                if (parentIndex == -1) return;

                int nextIndex = parentIndex + 1;

                // Remove columns after current
                RemoveColumnsFrom(nextIndex + 1);

                // Replace or add the new column FIRST → ProgressRing 즉시 표시
                if (nextIndex < Columns.Count)
                {
                    var oldColumn = Columns[nextIndex];
                    oldColumn.PropertyChanged -= FolderVm_PropertyChanged;
                    oldColumn.LoadError -= OnColumnLoadError;
                    oldColumn.CancelLoading();
                    oldColumn.SelectedChild = null;

                    // Defensive unsubscribe to prevent handler accumulation if folder instance is reused
                    folder.PropertyChanged -= FolderVm_PropertyChanged;
                    folder.LoadError -= OnColumnLoadError;
                    folder.PropertyChanged += FolderVm_PropertyChanged;
                    folder.LoadError += OnColumnLoadError;
                    Columns[nextIndex] = folder;
                }
                else
                {
                    AddColumn(folder);
                }

                CurrentPath = folder.Path;
                SelectedFile = null;

                // 로딩 중 ProgressRing 표시, 완료 시 항목 표시
                await folder.EnsureChildrenLoadedAsync();
                Helpers.DebugLogger.Log($"[NavigateIntoFolder] Navigation complete to: {folder.Path}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[NavigateIntoFolder] Error navigating to '{folder.Name}': {ex.Message}");
                // Propagate error to the folder's error state so UI can display it.
                // SetNavigationError → OnErrorMessageChanged → LoadError → OnColumnLoadError → NavigationError
                // 경로로 자동 전파되므로 NavigationError 직접 호출 불필요 (중복 토스트 방지)
                folder.SetNavigationError(ex.Message);
            }
        }

        /// <summary>
        /// Navigate to parent folder (called from Backspace key in Details/Icon views).
        /// </summary>
        public void NavigateUp()
        {
            if (CurrentFolder == null || string.IsNullOrEmpty(CurrentFolder.Path)) return;

            var currentPath = CurrentFolder.Path;

            // 원격 경로: URI에서 마지막 세그먼트 제거
            if (FileSystemRouter.IsRemotePath(currentPath))
            {
                var prefix = FileSystemRouter.GetUriPrefix(currentPath);
                var remotePath = FileSystemRouter.ExtractRemotePath(currentPath);
                if (remotePath == "/" || string.IsNullOrEmpty(remotePath)) return; // 루트에서 더 위로 올라갈 수 없음

                var parentRemote = remotePath.TrimEnd('/');
                var lastSlash = parentRemote.LastIndexOf('/');
                if (lastSlash <= 0) parentRemote = "/";
                else parentRemote = parentRemote.Substring(0, lastSlash);

                var parentUri = prefix + parentRemote;
                Helpers.DebugLogger.Log($"[NavigateUp] Remote: '{currentPath}' → '{parentUri}'");
                _ = NavigateToPath(parentUri);
                return;
            }

            var parentPath = System.IO.Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath)) return;

            // Check if parent directory exists
            if (!System.IO.Directory.Exists(parentPath)) return;

            Helpers.DebugLogger.Log($"[NavigateUp] Navigating from '{currentPath}' to '{parentPath}'");
            // NavigateToPath will handle PushToHistory internally
            _ = NavigateToPath(parentPath);
        }

        private void AddColumn(FolderViewModel folderVm)
        {
            // Defensive unsubscribe to prevent handler accumulation if re-added
            folderVm.PropertyChanged -= FolderVm_PropertyChanged;
            folderVm.LoadError -= OnColumnLoadError;
            folderVm.PropertyChanged += FolderVm_PropertyChanged;
            folderVm.LoadError += OnColumnLoadError;
            Columns.Add(folderVm);
        }

        private void OnColumnLoadError(string message) => NavigationError?.Invoke(message);

        /// <summary>
        /// Update IsOnPath only for items whose state actually changes.
        /// Instead of scanning ALL children in ALL columns (O(total_items)),
        /// we track previously highlighted items and only touch those + new ones (O(column_count)).
        /// </summary>
        private readonly List<FileSystemViewModel> _pathHighlightedItems = new();

        private void UpdatePathHighlights()
        {
            var accentBrush = FileSystemViewModel.GetPathHighlightBrush();

            // 1) Clear previous highlights (typically 3-8 items, not 14K)
            foreach (var prev in _pathHighlightedItems)
            {
                prev.IsOnPath = false;
                prev.PathBackground = FileSystemViewModel.TransparentBrush;
            }
            _pathHighlightedItems.Clear();

            // 2) Set new highlights (only SelectedChild of non-last columns)
            for (int i = 0; i < Columns.Count - 1; i++)
            {
                var selected = Columns[i].SelectedChild;
                if (selected != null)
                {
                    selected.IsOnPath = true;
                    selected.PathBackground = accentBrush;
                    _pathHighlightedItems.Add(selected);
                }
            }
        }

        /// <summary>
        /// Remove columns from index+1 onwards (keep columns[0..index]).
        /// </summary>
        private void RemoveColumnsFrom(int startIndex)
        {
            Helpers.DebugLogger.Log($"[RemoveColumnsFrom] Removing columns from index {startIndex}, current count: {Columns.Count}");

            for (int i = Columns.Count - 1; i >= startIndex; i--)
            {
                var column = Columns[i];
                Helpers.DebugLogger.Log($"[RemoveColumnsFrom] Removing column at index {i}: {column.Name}");

                column.PropertyChanged -= FolderVm_PropertyChanged;
                column.LoadError -= OnColumnLoadError;

                // 경량 초기화: 선택 해제만 수행, Children 및 _isLoaded 유지
                // 재방문 시 디스크 I/O 없이 즉시 표시 가능
                // (ResetState는 Cleanup/탭 닫기에서만 사용)
                column.CancelLoading();
                column.SelectedChild = null;

                // 썸네일 메모리 해제 — 뷰에서 제거된 컬럼의 BitmapImage 회수
                column.UnloadAllThumbnails();

                Columns.RemoveAt(i);
            }

            Helpers.DebugLogger.Log($"[RemoveColumnsFrom] Columns after removal: {string.Join(" > ", Columns.Select(c => c.Name))}");
        }

        /// <summary>
        /// Public wrapper for column cleanup - used by MainWindow for delete operations.
        /// </summary>
        public void CleanupColumnsFrom(int startIndex)
        {
            RemoveColumnsFrom(startIndex);
        }

        /// <summary>
        /// Notify that CurrentItems has changed (e.g. after ReloadAsync on the current folder).
        /// Needed because Details/List/Icon views bind to CurrentItems on ExplorerViewModel,
        /// and ReloadAsync replaces Children with a new ObservableCollection.
        /// </summary>
        public void NotifyCurrentItemsChanged()
        {
            OnPropertyChanged(nameof(CurrentFolder));
            OnPropertyChanged(nameof(CurrentItems));
        }

        private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                // When a column's Children collection is replaced (ReloadAsync / PopulateChildren),
                // notify CurrentItems so Details/List/Icon views rebind to the new collection.
                if (e.PropertyName == nameof(FolderViewModel.Children))
                {
                    // 정렬/필터 중에는 ApplyFilter 재적용만 차단 (연쇄 필터 방지)
                    // CurrentItems 통지는 항상 허용 — Details/List/Icon 뷰 바인딩에 필수
                    var isBulk = sender is FolderViewModel fvm && (fvm.IsSorting || fvm.IsBulkUpdating);

                    if (!isBulk && !string.IsNullOrEmpty(_filterText) && sender is FolderViewModel folderVm
                        && folderVm.CurrentFilterText != _filterText)
                    {
                        folderVm.ApplyFilter(_filterText);
                    }

                    if (sender == CurrentFolder)
                    {
                        OnPropertyChanged(nameof(CurrentItems));
                    }
                    return;
                }

                if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
                if (sender is not FolderViewModel parentFolder) return;

                // CRITICAL: Ignore selection changes during sorting to prevent tab flickering
                if (parentFolder.IsSorting) return;

                // CRITICAL: In Details/Icon mode, disable auto-navigation (only allow double-click)
                if (!EnableAutoNavigation) return;

                // CRITICAL: Suppress navigation when multiple items are selected
                if (parentFolder.HasMultiSelection) return;

                int parentIndex = Columns.IndexOf(parentFolder);
                if (parentIndex == -1) return;
                int nextIndex = parentIndex + 1;

                if (parentFolder.SelectedChild is FileViewModel fileVm)
                {
                    HandleFileSelection(fileVm, nextIndex);
                }
                else if (parentFolder.SelectedChild == null)
                {
                    HandleNullSelection(parentFolder, nextIndex);
                }
                else if (parentFolder.SelectedChild is FolderViewModel selectedFolder)
                {
                    await HandleFolderSelectionAsync(parentFolder, selectedFolder, parentIndex, nextIndex);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ExplorerViewModel] FolderVm_PropertyChanged error: {ex.Message}");
            }
        }

        private void HandleFileSelection(FileViewModel fileVm, int nextIndex)
        {
            RemoveColumnsFrom(nextIndex);
            // Finder behavior: tab shows parent folder name, not file name
            var parentDir = System.IO.Path.GetDirectoryName(fileVm.Path);
            if (!string.IsNullOrEmpty(parentDir))
                CurrentPath = parentDir;
            SelectedFile = fileVm;
            UpdatePathHighlights();
        }

        private void HandleNullSelection(FolderViewModel parentFolder, int nextIndex)
        {
            RemoveColumnsFrom(nextIndex);
            CurrentPath = parentFolder.Path;
            SelectedFile = null;
            UpdatePathHighlights();
        }

        private async Task HandleFolderSelectionAsync(
            FolderViewModel parentFolder, FolderViewModel selectedFolder,
            int parentIndex, int nextIndex)
        {
            // Cancel previous pending operation
            _selectionDebounce?.Cancel();
            _selectionDebounce = new CancellationTokenSource();
            var token = _selectionDebounce.Token;

            // 캐시 히트 시 디바운스 건너뜀 — 이미 로드된 폴더는 즉시 표시
            if (!selectedFolder.IsAlreadyLoaded)
            {
                try
                {
                    await Task.Delay(SelectionDebounceMs, token);
                }
                catch (OperationCanceledException) { return; }
                if (token.IsCancellationRequested) return;
            }

            try
            {
                // Validate state after await
                if (Columns.IndexOf(parentFolder) != parentIndex) return;
                if (parentFolder.SelectedChild != selectedFolder) return;

                // Push current path to history before changing (Miller auto-navigation)
                PushToHistory(selectedFolder.Path);

                RemoveColumnsFrom(nextIndex + 1);

                // 컬럼을 먼저 배치 → ProgressRing이 즉시 표시됨
                if (nextIndex < Columns.Count)
                {
                    var oldColumn = Columns[nextIndex];
                    oldColumn.PropertyChanged -= FolderVm_PropertyChanged;
                    oldColumn.LoadError -= OnColumnLoadError;
                    oldColumn.CancelLoading();
                    oldColumn.SelectedChild = null;

                    // Defensive unsubscribe to prevent handler accumulation if folder instance is reused
                    selectedFolder.PropertyChanged -= FolderVm_PropertyChanged;
                    selectedFolder.LoadError -= OnColumnLoadError;
                    selectedFolder.PropertyChanged += FolderVm_PropertyChanged;
                    selectedFolder.LoadError += OnColumnLoadError;
                    Columns[nextIndex] = selectedFolder;
                }
                else
                {
                    AddColumn(selectedFolder);
                }

                CurrentPath = selectedFolder.Path;
                SelectedFile = null;
                UpdatePathHighlights();

                // 컬럼이 UI에 보인 상태에서 로딩 (ProgressRing 표시)
                await selectedFolder.EnsureChildrenLoadedAsync();

                // Re-validate AFTER loading completes
                if (token.IsCancellationRequested) return;
                if (Columns.IndexOf(parentFolder) != parentIndex) return;
                if (parentFolder.SelectedChild != selectedFolder) return;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[HandleFolderSelectionAsync] 예외 발생 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// Get all selected items from the current (last) folder column.
        /// Supports both multi-selection and single-selection.
        /// </summary>
        public List<FileSystemViewModel> GetSelectedItems()
        {
            var folder = CurrentFolder;
            if (folder == null) return new List<FileSystemViewModel>();
            return folder.GetSelectedItemsList();
        }

        // ── Recursive Search ──

        [ObservableProperty]
        private bool _isRecursiveSearching;

        [ObservableProperty]
        private string _searchStatusText = "";

        private FolderViewModel? _searchResultFolder;
        private List<FolderViewModel>? _preSearchColumns;
        private string _preSearchPath = "";
        private CancellationTokenSource? _searchCts;

        /// <summary>
        /// 재귀 검색 시작: 현재 Columns/Path를 저장하고 가상 폴더에 결과를 스트리밍.
        /// </summary>
        public async Task StartRecursiveSearchAsync(SearchQuery query, string rootPath, bool showHidden)
        {
            // 1. 기존 검색 취소
            CancelRecursiveSearchInternal(restoreColumns: false);

            // 2. 현재 Columns/Path 저장 (Escape 복원용)
            _preSearchColumns = Columns.ToList();
            _preSearchPath = CurrentPath;

            // 3. 가상 FolderViewModel 생성
            var searchRootName = System.IO.Path.GetFileName(rootPath);
            if (string.IsNullOrEmpty(searchRootName))
                searchRootName = rootPath;

            var virtualFolder = new FolderItem
            {
                Name = $"검색 결과: {searchRootName}",
                Path = rootPath
            };
            _searchResultFolder = new FolderViewModel(virtualFolder, _fileService);
            _searchResultFolder.MarkAsManuallyPopulated();

            // 4. Columns 교체 → 가상 폴더 하나만
            foreach (var col in Columns)
            {
                col.PropertyChanged -= FolderVm_PropertyChanged;
                col.LoadError -= OnColumnLoadError;
                col.CancelLoading();
                col.SelectedChild = null;
            }
            Columns.Clear();

            AddColumn(_searchResultFolder);
            CurrentPath = rootPath;
            SelectedFile = null;
            OnPropertyChanged(nameof(HasActiveSearchResults));

            // 5. 검색 시작 (백그라운드 스레드에서 실행)
            IsRecursiveSearching = true;
            SearchStatusText = "검색 중...";

            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            var searchService = new RecursiveSearchService(_fileService);
            var progress = new Progress<RecursiveSearchService.SearchProgress>(p =>
            {
                SearchStatusText = $"검색 중... {p.FilesFound}개 발견 ({p.FoldersScanned}개 폴더)";
            });

            // Channel 기반: 백그라운드에서 검색, UI 스레드에서 배치 수신
            var reader = searchService.SearchInBackground(rootPath, query, showHidden, progress, ct);

            int count = 0;
            bool limitReached = false;

            try
            {
                await foreach (var batch in reader.ReadAllAsync(ct))
                {
                    if (ct.IsCancellationRequested) break;
                    if (_searchResultFolder == null) break;

                    // 배치 단위로 추가 — 개별 Add보다 UI 갱신 빈도가 낮아짐
                    foreach (var item in batch)
                    {
                        if (count >= RecursiveSearchService.MaxResults)
                        {
                            limitReached = true;
                            break;
                        }
                        _searchResultFolder.Children.Add(item);
                        count++;
                    }

                    if (limitReached) break;

                    // 배치 처리 후 UI 양보
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Threading.Channels.ChannelClosedException) { }

            if (!ct.IsCancellationRequested)
            {
                if (limitReached)
                    SearchStatusText = $"검색 완료: {count}개 발견 (결과 제한: 최대 {RecursiveSearchService.MaxResults}개)";
                else
                    SearchStatusText = count > 0
                        ? $"검색 완료: {count}개 발견"
                        : "검색 결과 없음";

                IsRecursiveSearching = false;
            }
        }

        /// <summary>
        /// 재귀 검색 취소 + 원래 Columns/Path 복원.
        /// </summary>
        public void CancelRecursiveSearch()
        {
            CancelRecursiveSearchInternal(restoreColumns: true);
        }

        private void CancelRecursiveSearchInternal(bool restoreColumns)
        {
            // CTS 취소
            if (_searchCts != null)
            {
                _searchCts.Cancel();
                _searchCts.Dispose();
                _searchCts = null;
            }

            IsRecursiveSearching = false;
            SearchStatusText = "";

            // 가상 폴더 정리
            bool hadSearchResults = _searchResultFolder != null;
            if (_searchResultFolder != null)
            {
                _searchResultFolder.PropertyChanged -= FolderVm_PropertyChanged;
                _searchResultFolder.LoadError -= OnColumnLoadError;
                _searchResultFolder.CancelLoading();
                _searchResultFolder = null;
            }
            if (hadSearchResults)
                OnPropertyChanged(nameof(HasActiveSearchResults));

            // 원래 Columns/Path 복원
            if (restoreColumns && _preSearchColumns != null)
            {
                foreach (var col in Columns)
                {
                    col.PropertyChanged -= FolderVm_PropertyChanged;
                    col.LoadError -= OnColumnLoadError;
                    col.CancelLoading();
                    col.SelectedChild = null;
                }
                Columns.Clear();

                foreach (var col in _preSearchColumns)
                {
                    AddColumn(col);
                }

                CurrentPath = _preSearchPath;
                SelectedFile = null;

                _preSearchColumns = null;
                _preSearchPath = "";
            }
        }

        /// <summary>
        /// 현재 재귀 검색 결과 폴더인지 확인.
        /// </summary>
        public bool HasActiveSearchResults => _searchResultFolder != null;

        /// <summary>
        /// Clean up all resources when closing the application.
        /// </summary>
        public void Cleanup()
        {
            Helpers.DebugLogger.Log("[ExplorerViewModel.Cleanup] Starting cleanup...");

            // CRITICAL: Suppress CollectionChanged → PropertyChanged BEFORE clearing.
            // Without this, Columns.Clear() fires CollectionChanged, which fires
            // PropertyChanged(CurrentFolder/CurrentItems), reaching disposed UI → crash.
            _isCleaningUp = true;

            // Cancel any pending debounce operations
            _selectionDebounce?.Cancel();
            _selectionDebounce?.Dispose();
            _selectionDebounce = null;

            // Cancel any active recursive search
            CancelRecursiveSearchInternal(restoreColumns: false);
            _preSearchColumns = null;

            // Clean up all columns — release thumbnails and child references
            if (Columns != null)
            {
                foreach (var column in Columns.ToList())
                {
                    column.PropertyChanged -= FolderVm_PropertyChanged;
                    column.LoadError -= OnColumnLoadError;
                    column.CancelLoading();
                    column.ClearChildren();
                }
                Columns.Clear();
            }

            // Clear inline preview state
            SelectedFile = null;

            // Clear navigation history
            _backStack.Clear();
            _forwardStack.Clear();

            Helpers.DebugLogger.Log("[ExplorerViewModel.Cleanup] Cleanup complete");
        }
    }
}
