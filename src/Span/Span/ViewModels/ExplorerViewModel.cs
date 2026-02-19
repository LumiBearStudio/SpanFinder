using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        }

        private void UpdatePathSegments(string path)
        {
            PathSegments.Clear();
            if (string.IsNullOrWhiteSpace(path)) return;

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

        /// <summary>
        /// Navigate to a folder from sidebar (reset all columns).
        /// </summary>
        public async void NavigateTo(FolderItem folder)
        {
            Helpers.DebugLogger.Log($"[NavigateTo] Navigating to: {folder.Name}, clearing {Columns.Count} columns");

            // Cleanup all - reset state before removing
            foreach (var col in Columns)
            {
                col.PropertyChanged -= FolderVm_PropertyChanged;
                col.ResetState();
            }
            Columns.Clear();

            var rootVm = new FolderViewModel(folder, _fileService);
            await rootVm.EnsureChildrenLoadedAsync();

            AddColumn(rootVm);
            CurrentPath = rootVm.Path;

            Helpers.DebugLogger.Log($"[NavigateTo] Navigation complete. Current path: {CurrentPath}");
        }

        /// <summary>
        /// 문자열 경로로 직접 탐색 (주소 표시줄 편집, 브레드크럼 클릭).
        /// </summary>
        public async void NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!System.IO.Directory.Exists(path)) return;

            var folderItem = new FolderItem { Name = System.IO.Path.GetFileName(path), Path = path };
            NavigateTo(folderItem);
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

                // UI 갱신을 위해 PropertyChanged 알림이 필요할 수 있음.
                // RemoveColumnsFrom 내부에서 CollectionChanged가 발생하므로 UI는 줄어듦.
            }
            else
            {
                // 3. 컬럼에 없다면 (완전히 다른 경로로 점프하는 경우) 기존 방식대로 전체 이동
                NavigateToPath(segment.FullPath);
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
                oldColumn.ResetState();

                folder.PropertyChanged += FolderVm_PropertyChanged;
                Columns[nextIndex] = folder;
            }
            else
            {
                AddColumn(folder);
            }

            CurrentPath = folder.Path;
            Helpers.DebugLogger.Log($"[NavigateIntoFolder] Navigation complete to: {folder.Path}");
        }

        /// <summary>
        /// Navigate to parent folder (called from Backspace key in Details/Icon views).
        /// </summary>
        public void NavigateUp()
        {
            if (CurrentFolder == null || string.IsNullOrEmpty(CurrentFolder.Path)) return;

            var parentPath = System.IO.Path.GetDirectoryName(CurrentFolder.Path);
            if (string.IsNullOrEmpty(parentPath)) return;

            // Check if parent directory exists
            if (!System.IO.Directory.Exists(parentPath)) return;

            Helpers.DebugLogger.Log($"[NavigateUp] Navigating from '{CurrentFolder.Path}' to '{parentPath}'");
            NavigateToPath(parentPath);
        }

        private void AddColumn(FolderViewModel folderVm)
        {
            folderVm.PropertyChanged += FolderVm_PropertyChanged;
            Columns.Add(folderVm);
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

                // Reset state so folder reloads fresh when revisited
                column.ResetState();

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

        private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
            if (sender is not FolderViewModel parentFolder) return;

            // CRITICAL: Ignore selection changes during sorting to prevent tab flickering
            if (parentFolder.IsSorting)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Ignoring selection change during sorting");
                return;
            }

            // CRITICAL: In Details/Icon mode, disable auto-navigation (only allow double-click)
            if (!EnableAutoNavigation)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Auto-navigation disabled - selection only");
                return;
            }

            // CRITICAL: Suppress navigation when multiple items are selected
            if (parentFolder.HasMultiSelection)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Multi-selection active ({parentFolder.SelectedItems.Count} items) - suppressing navigation");
                return;
            }

            Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Selection changed in '{parentFolder.Name}' to '{parentFolder.SelectedChild?.Name ?? "null"}'");

            int parentIndex = Columns.IndexOf(parentFolder);
            if (parentIndex == -1)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Parent folder not in Columns - ABORT");
                return;
            }
            int nextIndex = parentIndex + 1;

            Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] ParentIndex: {parentIndex}, NextIndex: {nextIndex}");

            // File or null selection: immediate processing (no debouncing)
            if (parentFolder.SelectedChild is FileViewModel fileVm)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] File selected: {fileVm.Name} - removing columns from {nextIndex}");
                RemoveColumnsFrom(nextIndex);
                CurrentPath = fileVm.Path;
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] ===== FILE SELECTION COMPLETE =====");
                return;
            }

            if (parentFolder.SelectedChild == null)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Null selection - removing columns from {nextIndex}");
                RemoveColumnsFrom(nextIndex);
                CurrentPath = parentFolder.Path;
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] ===== NULL SELECTION COMPLETE =====");
                return;
            }

            // Folder selection: apply debouncing
            if (parentFolder.SelectedChild is FolderViewModel selectedFolder)
            {
                Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Folder selected: {selectedFolder.Name} - applying {SelectionDebounceMs}ms debounce");

                // Cancel previous pending operation
                _selectionDebounce?.Cancel();
                _selectionDebounce = new CancellationTokenSource();
                var token = _selectionDebounce.Token;

                try
                {
                    await Task.Delay(SelectionDebounceMs, token);
                    if (token.IsCancellationRequested)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Debounce cancelled - ABORT");
                        return;
                    }

                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Debounce complete, validating state...");

                    // Validate state after await
                    if (Columns.IndexOf(parentFolder) != parentIndex)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Parent index changed - ABORT");
                        return;
                    }
                    if (parentFolder.SelectedChild != selectedFolder)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Selection changed during debounce - ABORT");
                        return;
                    }

                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Loading children for {selectedFolder.Name}...");
                    await selectedFolder.EnsureChildrenLoadedAsync();

                    // Re-validate AFTER loading completes
                    if (token.IsCancellationRequested)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Cancelled after loading - ABORT");
                        return;
                    }
                    if (Columns.IndexOf(parentFolder) != parentIndex)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Parent index changed after loading - ABORT");
                        return;
                    }
                    if (parentFolder.SelectedChild != selectedFolder)
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Selection changed after loading - ABORT");
                        return;
                    }

                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Removing columns from {nextIndex + 1}");
                    RemoveColumnsFrom(nextIndex + 1);

                    // Replace or Add
                    if (nextIndex < Columns.Count)
                    {
                        var oldColumn = Columns[nextIndex];
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Replacing column at index {nextIndex}: '{oldColumn.Name}' → '{selectedFolder.Name}'");

                        // CRITICAL FIX: Reset old column state before replacing
                        // This ensures the old FolderViewModel will reload fresh data when selected again
                        oldColumn.PropertyChanged -= FolderVm_PropertyChanged;
                        oldColumn.ResetState();

                        selectedFolder.PropertyChanged += FolderVm_PropertyChanged;
                        Columns[nextIndex] = selectedFolder;
                    }
                    else
                    {
                        Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Adding new column {selectedFolder.Name} at index {nextIndex}");
                        AddColumn(selectedFolder);
                    }

                    CurrentPath = selectedFolder.Path;
                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] CurrentPath updated to: {CurrentPath}");
                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] Columns: {string.Join(" > ", Columns.Select(c => c.Name))}");
                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] ===== FOLDER SELECTION COMPLETE =====");
                }
                catch (TaskCanceledException)
                {
                    Helpers.DebugLogger.Log($"[FolderVm_PropertyChanged] TaskCanceledException caught");
                }
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

            // Clean up all columns
            if (Columns != null)
            {
                foreach (var column in Columns.ToList())
                {
                    column.PropertyChanged -= FolderVm_PropertyChanged;
                    column.CancelLoading();
                }
                Columns.Clear();
            }

            Helpers.DebugLogger.Log("[ExplorerViewModel.Cleanup] Cleanup complete");
        }
    }
}
