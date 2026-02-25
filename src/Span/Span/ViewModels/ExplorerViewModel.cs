using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Span.Models;
using Span.Services;

namespace Span.ViewModels
{
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
                col.CancelLoading();
                col.SelectedChild = null;
            }
            Columns.Clear();

            var rootVm = new FolderViewModel(folder, _fileService);
            await rootVm.EnsureChildrenLoadedAsync();

            AddColumn(rootVm);
            CurrentPath = rootVm.Path;
            SelectedFile = null;

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

            // 원격 경로: Directory.Exists 스킵, URI 그대로 사용
            if (FileSystemRouter.IsRemotePath(path))
            {
                await NavigateToRemotePath(path);
                return;
            }

            if (!System.IO.Directory.Exists(path)) return;

            // Normalize path (guard against PathTooLongException)
            try { path = System.IO.Path.GetFullPath(path); }
            catch (System.IO.PathTooLongException) { return; }

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

            Helpers.DebugLogger.Log($"[NavigateToPath] Building full hierarchy for: {path} ({parts.Length + 1} levels)");

            // Suppress auto-navigation while building column hierarchy
            var previousAutoNav = EnableAutoNavigation;
            EnableAutoNavigation = false;

            try
            {
                // 경량 정리 — Children 유지, 구독만 해제
                foreach (var col in Columns)
                {
                    col.PropertyChanged -= FolderVm_PropertyChanged;
                    col.CancelLoading();
                    col.SelectedChild = null;
                }
                Columns.Clear();

                // Create root column (drive)
                var rootFolder = new FolderItem { Name = root.TrimEnd('\\'), Path = root };
                var currentVm = new FolderViewModel(rootFolder, _fileService);
                await currentVm.EnsureChildrenLoadedAsync();
                AddColumn(currentVm);

                // Build columns for each path segment
                for (int i = 0; i < parts.Length; i++)
                {
                    // Find matching child folder in current column
                    var childVm = currentVm.Children.OfType<FolderViewModel>()
                        .FirstOrDefault(c => string.Equals(c.Name, parts[i], System.StringComparison.OrdinalIgnoreCase));

                    if (childVm == null)
                    {
                        Helpers.DebugLogger.Log($"[NavigateToPath] Segment '{parts[i]}' not found in '{currentVm.Path}' - stopping");
                        break;
                    }

                    // Select child in parent column (visual highlight)
                    currentVm.SelectedChild = childVm;

                    // Load children and add as next column
                    await childVm.EnsureChildrenLoadedAsync();
                    AddColumn(childVm);

                    currentVm = childVm;
                }

                // Set current path to the last successfully loaded column
                CurrentPath = currentVm.Path;
                SelectedFile = null;

                Helpers.DebugLogger.Log($"[NavigateToPath] Hierarchy built: {string.Join(" > ", Columns.Select(c => c.Name))}");
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

            Helpers.DebugLogger.Log($"[NavigateIntoFolder] Manual navigation to: {folder.Name}");

            // Push current path to history before navigating
            PushToHistory(folder.Path);

            // Load children first
            await folder.EnsureChildrenLoadedAsync();

            // Find parent column index
            var parentFolder = fromColumn ?? CurrentFolder;
            if (parentFolder == null) return;

            int parentIndex = Columns.IndexOf(parentFolder);
            if (parentIndex == -1) return;

            int nextIndex = parentIndex + 1;

            // Remove columns after current
            RemoveColumnsFrom(nextIndex + 1);

            // Replace or add the new column
            if (nextIndex < Columns.Count)
            {
                var oldColumn = Columns[nextIndex];
                oldColumn.PropertyChanged -= FolderVm_PropertyChanged;
                oldColumn.CancelLoading();
                oldColumn.SelectedChild = null;

                folder.PropertyChanged += FolderVm_PropertyChanged;
                Columns[nextIndex] = folder;
            }
            else
            {
                AddColumn(folder);
            }

            CurrentPath = folder.Path;
            SelectedFile = null;
            Helpers.DebugLogger.Log($"[NavigateIntoFolder] Navigation complete to: {folder.Path}");
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
            folderVm.PropertyChanged += FolderVm_PropertyChanged;
            Columns.Add(folderVm);
        }

        /// <summary>
        /// Update IsOnPath for all items in all columns.
        /// Items that are the SelectedChild of a non-last column are "on the path".
        /// </summary>
        private void UpdatePathHighlights()
        {
            var accentBrush = FileSystemViewModel.GetAccentDimBrush();

            for (int i = 0; i < Columns.Count; i++)
            {
                var column = Columns[i];
                bool isParentColumn = i < Columns.Count - 1;

                foreach (var child in column.Children)
                {
                    bool onPath = isParentColumn && child == column.SelectedChild;
                    child.IsOnPath = onPath;
                    child.PathBackground = onPath ? accentBrush : FileSystemViewModel.TransparentBrush;
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
            // When a column's Children collection is replaced (ReloadAsync / PopulateChildren),
            // notify CurrentItems so Details/List/Icon views rebind to the new collection.
            if (e.PropertyName == nameof(FolderViewModel.Children))
            {
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

        private void HandleFileSelection(FileViewModel fileVm, int nextIndex)
        {
            RemoveColumnsFrom(nextIndex);
            CurrentPath = fileVm.Path;
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

                await selectedFolder.EnsureChildrenLoadedAsync();

                // Re-validate AFTER loading completes
                if (token.IsCancellationRequested) return;
                if (Columns.IndexOf(parentFolder) != parentIndex) return;
                if (parentFolder.SelectedChild != selectedFolder) return;

                // Push current path to history before changing (Miller auto-navigation)
                PushToHistory(selectedFolder.Path);

                RemoveColumnsFrom(nextIndex + 1);

                // Replace or Add
                if (nextIndex < Columns.Count)
                {
                    var oldColumn = Columns[nextIndex];
                    oldColumn.PropertyChanged -= FolderVm_PropertyChanged;
                    oldColumn.CancelLoading();
                    oldColumn.SelectedChild = null;

                    selectedFolder.PropertyChanged += FolderVm_PropertyChanged;
                    Columns[nextIndex] = selectedFolder;
                }
                else
                {
                    AddColumn(selectedFolder);
                }

                CurrentPath = selectedFolder.Path;
                SelectedFile = null;
                UpdatePathHighlights();
            }
            catch (TaskCanceledException) { }
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

            // Clean up all columns — release thumbnails and child references
            if (Columns != null)
            {
                foreach (var column in Columns.ToList())
                {
                    column.PropertyChanged -= FolderVm_PropertyChanged;
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
