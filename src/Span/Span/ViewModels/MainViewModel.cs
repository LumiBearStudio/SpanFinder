using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text.Json;

namespace Span.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appTitle = "Span";

        public ObservableCollection<TabItem> Tabs { get; } = new();
        public ObservableCollection<DriveItem> Drives { get; } = new();
        public ObservableCollection<DriveItem> NetworkDrives { get; } = new();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new();
        public ObservableCollection<FavoriteItem> RecentFolders { get; } = new();
        public ObservableCollection<Models.ConnectionInfo> SavedConnections { get; } = new();

        /// <summary>
        /// 사이드바 "드라이브" 섹션에 표시할 통합 컬렉션 (로컬 + 네트워크 + 원격 연결)
        /// </summary>
        public ObservableCollection<DriveItem> AllDrives { get; } = new();

        // Engine — Split View (Dual-Pane)
        private ExplorerViewModel _leftExplorer;
        public ExplorerViewModel LeftExplorer
        {
            get => _leftExplorer;
            set
            {
                var old = _leftExplorer;
                if (SetProperty(ref _leftExplorer, value))
                {
                    // PropertyChanged 구독 교체 (old → new)
                    if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
                    if (value != null) value.PropertyChanged += OnLeftExplorerPropertyChanged;

                    OnPropertyChanged(nameof(Explorer));
                    OnPropertyChanged(nameof(ActiveExplorer));
                }
            }
        }

        private ExplorerViewModel _rightExplorer;
        public ExplorerViewModel RightExplorer
        {
            get => _rightExplorer;
            set => SetProperty(ref _rightExplorer, value);
        }

        /// <summary>
        /// Backward-compat: always returns LeftExplorer.
        /// XAML bindings for the left/single pane use this.
        /// </summary>
        public ExplorerViewModel Explorer => LeftExplorer;

        /// <summary>
        /// Returns the explorer for the currently active pane.
        /// Code-behind operations should use this instead of Explorer.
        /// </summary>
        public ExplorerViewModel ActiveExplorer =>
            ActivePane == ActivePane.Left ? LeftExplorer : RightExplorer;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveExplorer))]
        private ActivePane _activePane = ActivePane.Left;

        [ObservableProperty]
        private bool _isSplitViewEnabled = false;

        [ObservableProperty]
        private ViewMode _leftViewMode = ViewMode.MillerColumns;

        [ObservableProperty]
        private ViewMode _rightViewMode = ViewMode.MillerColumns;

        // Preview panel state (per-pane independent)
        [ObservableProperty]
        private bool _isLeftPreviewEnabled = false;

        [ObservableProperty]
        private bool _isRightPreviewEnabled = false;

        private readonly FileSystemService _fileService;
        private readonly FavoritesService _favoritesService;
        private readonly ActionLogService _actionLogService;
        private readonly FileOperationHistory _operationHistory;
        private readonly FileOperationProgressViewModel _progressViewModel;
        private readonly FileOperationManager _fileOperationManager;
        private readonly System.Threading.CancellationTokenSource _shutdownCts = new();
        private bool _isCleaningUp = false;
        private const int MaxRecentFolders = 20;

        [ObservableProperty]
        private bool _canUndo = false;

        [ObservableProperty]
        private bool _canRedo = false;

        [ObservableProperty]
        private string? _undoDescription;

        [ObservableProperty]
        private string? _redoDescription;

        // Navigation history — forwarded from ActiveExplorer
        [ObservableProperty]
        private bool _canGoBack = false;

        [ObservableProperty]
        private bool _canGoForward = false;

        [ObservableProperty]
        private string _statusBarText = string.Empty;

        // Status bar — item count, selection count, view mode display
        [ObservableProperty]
        private string _statusItemCountText = "";

        [ObservableProperty]
        private string _statusSelectionText = "";

        [ObservableProperty]
        private string _statusViewModeText = "";

        [ObservableProperty]
        private string _statusDiskSpaceText = "";

        [ObservableProperty]
        private string _toastMessage = string.Empty;

        [ObservableProperty]
        private bool _isToastVisible = false;

        private System.Threading.Timer? _toastTimer;

        [ObservableProperty]
        private ViewMode _currentViewMode = ViewMode.MillerColumns;

        [ObservableProperty]
        private ViewMode _currentIconSize = ViewMode.IconMedium; // Icon 모드 기본 크기

        [ObservableProperty]
        private int _activeTabIndex = 0;

        public TabItem? ActiveTab => (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count) ? Tabs[ActiveTabIndex] : null;

        /// <summary>
        /// 탭 전환 중 PropertyChanged 연쇄 반응(FocusActiveView, ScrollToLastColumn 등) 억제용 플래그.
        /// MainWindow에서 읽어서 불필요한 UI 작업을 건너뛴다.
        /// </summary>
        public bool IsSwitchingTab { get; private set; }

        public FileOperationProgressViewModel ProgressViewModel => _progressViewModel;
        public FileOperationManager FileOperationManager => _fileOperationManager;

        public MainViewModel(FileSystemService fileService, FavoritesService favoritesService, ActionLogService actionLogService)
        {
            _fileService = fileService;
            _favoritesService = favoritesService;
            _actionLogService = actionLogService;
            _operationHistory = new FileOperationHistory();
            _progressViewModel = new FileOperationProgressViewModel();
            _fileOperationManager = App.Current.Services.GetRequiredService<FileOperationManager>();
            _progressViewModel.OperationManager = _fileOperationManager;

            // Apply UndoHistorySize setting
            var settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
            _operationHistory.MaxHistorySize = settings.UndoHistorySize;
            settings.SettingChanged += (key, value) =>
            {
                if (key == "UndoHistorySize" && value is int size)
                    _operationHistory.MaxHistorySize = size;
            };

            _operationHistory.HistoryChanged += OnHistoryChanged;

            Initialize();
        }

        /// <summary>
        /// LeftExplorer의 PropertyChanged 핸들러 — setter에서 구독/해제 관리
        /// </summary>
        private void OnLeftExplorerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_isCleaningUp) return;
            if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(LeftExplorer?.CurrentPath))
            {
                AddRecentFolder(LeftExplorer.CurrentPath);
                UpdateActiveTabHeader();
            }

            // CurrentFolder/CurrentItems changed → update item count
            if (e.PropertyName == nameof(ExplorerViewModel.CurrentFolder) ||
                e.PropertyName == nameof(ExplorerViewModel.CurrentItems))
            {
                UpdateStatusBar();
            }

            // Forward navigation history state from ActiveExplorer
            if (e.PropertyName == nameof(ExplorerViewModel.CanGoBack) ||
                e.PropertyName == nameof(ExplorerViewModel.CanGoForward))
            {
                SyncNavigationHistoryState();
            }
        }

        /// <summary>
        /// 상태바 텍스트 갱신 — item count, selection count, view mode.
        /// Code-behind에서 선택 변경 시에도 호출 가능.
        /// </summary>
        public void UpdateStatusBar()
        {
            if (_isCleaningUp) return;

            var explorer = ActiveExplorer;
            var folder = explorer?.CurrentFolder;
            int itemCount = folder?.Children?.Count ?? 0;

            // Selection: use SelectedItems (multi) or SelectedChild (single)
            int selCount = 0;
            if (folder != null)
            {
                if (folder.HasMultiSelection)
                    selCount = folder.SelectedItems.Count;
                else if (folder.SelectedChild != null)
                    selCount = 1;
            }

            StatusItemCountText = $"{itemCount}개 항목";
            StatusSelectionText = selCount > 0 ? $"{selCount}개 선택됨" : "";

            var mode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            StatusViewModeText = Helpers.ViewModeExtensions.GetDisplayName(mode);

            // Disk space info
            StatusDiskSpaceText = GetDiskSpaceText(explorer?.CurrentPath);
        }

        /// <summary>
        /// Get disk space text for the drive containing the given path.
        /// Returns empty string if path is null or drive info cannot be determined.
        /// </summary>
        private static string GetDiskSpaceText(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            try
            {
                var root = System.IO.Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return "";

                var driveInfo = new System.IO.DriveInfo(root);
                if (!driveInfo.IsReady) return "";

                return $"{FormatDiskSize(driveInfo.AvailableFreeSpace)} free / {FormatDiskSize(driveInfo.TotalSize)}";
            }
            catch
            {
                // Network paths, invalid drives, etc.
                return "";
            }
        }

        private static string FormatDiskSize(long bytes)
        {
            if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
            return $"{bytes / (double)(1L << 10):F1} KB";
        }

        /// <summary>
        /// Sync CanGoBack/CanGoForward from the current ActiveExplorer.
        /// Called when explorer changes or when history state changes.
        /// </summary>
        public void SyncNavigationHistoryState()
        {
            if (_isCleaningUp) return;
            var explorer = ActiveExplorer;
            CanGoBack = explorer?.CanGoBack ?? false;
            CanGoForward = explorer?.CanGoForward ?? false;
        }

        /// <summary>
        /// Navigate back in the active explorer's history.
        /// </summary>
        public async Task GoBackAsync()
        {
            var explorer = ActiveExplorer;
            if (explorer == null || !explorer.CanGoBack) return;
            await explorer.GoBack();
            SyncNavigationHistoryState();
        }

        /// <summary>
        /// Navigate forward in the active explorer's history.
        /// </summary>
        public async Task GoForwardAsync()
        {
            var explorer = ActiveExplorer;
            if (explorer == null || !explorer.CanGoForward) return;
            await explorer.GoForward();
            SyncNavigationHistoryState();
        }

        private void Initialize()
        {
            // Create default tab (will be replaced by LoadTabsFromSettings on Loaded)
            EnsureDefaultTab();

            // Initialize Engines with a conceptual Root
            var root = new FolderItem { Name = "PC", Path = "PC" };
            LeftExplorer = new ExplorerViewModel(root, _fileService);
            // 첫 번째 탭에 ExplorerViewModel 할당
            Tabs[0].Explorer = LeftExplorer;

            var rightRoot = new FolderItem { Name = "PC", Path = "PC" };
            RightExplorer = new ExplorerViewModel(rightRoot, _fileService);

            // Populate Sidebar
            LoadDrives();
            LoadFavorites();
            LoadRecentFolders();
            LoadSavedConnections();

            // Load ViewMode preference (includes split state)
            LoadViewModePreference();

            // LeftExplorer PropertyChanged는 setter에서 자동 구독됨
            // RightExplorer는 탭과 무관하므로 별도 구독
            RightExplorer.PropertyChanged += (s, e) =>
            {
                if (_isCleaningUp) return;
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(RightExplorer.CurrentPath))
                {
                    AddRecentFolder(RightExplorer.CurrentPath);
                }
            };
        }

        private void EnsureDefaultTab()
        {
            if (Tabs.Count == 0)
            {
                var tab = new TabItem { Header = "Home", ViewMode = ViewMode.Home, IsActive = true };
                Tabs.Add(tab);
                ActiveTabIndex = 0;
            }
        }

        /// <summary>
        /// Refresh drives list (called on device change events like USB plug/unplug)
        /// </summary>
        public void RefreshDrives()
        {
            if (_isCleaningUp || _shutdownCts.Token.IsCancellationRequested) return;
            LoadDrives();
            Helpers.DebugLogger.Log("[MainViewModel] RefreshDrives triggered by device change");
        }

        private async void LoadDrives()
        {
            try
            {
                // Step 1: Load from cache immediately (fast — no I/O, no async)
                var cachedDrives = LoadDrivesFromCache();
                if (cachedDrives.Count > 0)
                {
                    Drives.Clear();
                    NetworkDrives.Clear();
                    foreach (var drive in cachedDrives)
                    {
                        if (drive.IsNetworkDrive)
                            NetworkDrives.Add(drive);
                        else
                            Drives.Add(drive);
                    }
                    Helpers.DebugLogger.Log($"[MainViewModel] Loaded {cachedDrives.Count} drives from cache");
                    RebuildAllDrives();
                }

                // Step 2: Refresh from file system in background (accurate)
                // GetDrivesAsync now runs DriveInfo.GetDrives() off UI thread
                var drives = await _fileService.GetDrivesAsync();

                // Step 3: Check if we're shutting down before updating UI
                if (_shutdownCts.Token.IsCancellationRequested)
                {
                    Helpers.DebugLogger.Log("[MainViewModel] LoadDrives cancelled - app is shutting down");
                    return;
                }

                // Step 4: Skip UI update if drives haven't changed (avoid flicker)
                var newLocalDrives = drives.Where(d => !d.IsNetworkDrive).ToList();
                var newNetworkDrives = drives.Where(d => d.IsNetworkDrive).ToList();

                bool drivesChanged = !AreDriveListsEqual(Drives, newLocalDrives)
                                  || !AreDriveListsEqual(NetworkDrives, newNetworkDrives);

                if (drivesChanged)
                {
                    Drives.Clear();
                    NetworkDrives.Clear();
                    foreach (var drive in newLocalDrives)
                        Drives.Add(drive);
                    foreach (var drive in newNetworkDrives)
                        NetworkDrives.Add(drive);
                }

                // Step 5: Save updated list to cache
                SaveDrivesCache(drives);
                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {Drives.Count} local + {NetworkDrives.Count} network drives (changed={drivesChanged})");

                // Step 6: Rebuild unified AllDrives collection
                RebuildAllDrives();
            }
            catch (System.OperationCanceledException)
            {
                Helpers.DebugLogger.Log("[MainViewModel] LoadDrives cancelled");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] LoadDrives error: {ex.Message}");
            }
        }

        /// <summary>
        /// 로컬 + 네트워크 + 원격 연결을 AllDrives에 통합
        /// </summary>
        private void RebuildAllDrives()
        {
            AllDrives.Clear();
            foreach (var d in Drives)
                AllDrives.Add(d);
            foreach (var d in NetworkDrives)
                AllDrives.Add(d);
            foreach (var conn in SavedConnections)
                AllDrives.Add(DriveItem.FromConnection(conn));
        }

        /// <summary>
        /// Compare two drive lists by Path to detect changes
        /// </summary>
        private static bool AreDriveListsEqual(
            System.Collections.ObjectModel.ObservableCollection<DriveItem> current,
            List<DriveItem> updated)
        {
            if (current.Count != updated.Count) return false;
            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i].Path, updated[i].Path, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Cleanup resources when app is closing
        /// </summary>
        public void Cleanup()
        {
            try
            {
                Helpers.DebugLogger.Log("[MainViewModel] Starting cleanup...");

                // Save tab state before cleanup
                SaveActiveTabState();
                SaveTabsToSettings();

                // Save state before suppressing notifications
                _favoritesService.SaveFavorites(Favorites.ToList());
                SaveRecentFolders();
                SaveSplitViewState();
                SavePreviewState();

                // MUST set before clearing collections to prevent
                // ObservableCollection change notifications reaching disposed UI
                _isCleaningUp = true;

                // Cancel any ongoing background operations
                _shutdownCts?.Cancel();
                _toastTimer?.Dispose();

                // 모든 탭의 Explorer 정리
                foreach (var tab in Tabs)
                    tab.Explorer?.Cleanup();

                // 활성 원격 연결 모두 해제
                try
                {
                    var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
                    router.DisconnectAll();
                    Helpers.DebugLogger.Log("[MainViewModel] All remote connections disconnected");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainViewModel] Error disconnecting remote connections: {ex.Message}");
                }

                // Clear collections (safe now - _isCleaningUp suppresses side effects)
                AllDrives.Clear();
                Drives.Clear();
                NetworkDrives.Clear();
                Tabs.Clear();
                Favorites.Clear();
                RecentFolders.Clear();

                Helpers.DebugLogger.Log("[MainViewModel] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load drives from LocalSettings cache (instant)
        /// </summary>
        private List<DriveItem> LoadDrivesFromCache()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values["DrivesCacheJson"] is string json && !string.IsNullOrEmpty(json))
                {
                    var drives = JsonSerializer.Deserialize<List<DrivesCacheDto>>(json);
                    if (drives != null)
                    {
                        return drives.Select(d => new DriveItem
                        {
                            Name = d.Name ?? "",
                            Path = d.Path ?? "",
                            Label = d.Label ?? "",
                            TotalSize = d.TotalSize,
                            AvailableFreeSpace = d.AvailableFreeSpace,
                            DriveFormat = d.DriveFormat ?? "",
                            DriveType = d.DriveType ?? "",
                            IconGlyph = Services.IconService.Current?.DriveGlyph ?? "\uEC65"
                        }).ToList();
                    }
                }

                // Clean up old corrupted CompositeValue format if present
                if (settings.Values.ContainsKey("DrivesCache"))
                    settings.Values.Remove("DrivesCache");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading drives from cache: {ex.Message}");
            }

            return new List<DriveItem>();
        }

        /// <summary>
        /// Save drives to LocalSettings cache (JSON format)
        /// </summary>
        private void SaveDrivesCache(List<DriveItem> drives)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var dtos = drives.Select(d => new DrivesCacheDto
                {
                    Name = d.Name, Path = d.Path, Label = d.Label,
                    TotalSize = d.TotalSize, AvailableFreeSpace = d.AvailableFreeSpace,
                    DriveFormat = d.DriveFormat, DriveType = d.DriveType,
                    IconGlyph = d.IconGlyph
                }).ToList();

                settings.Values["DrivesCacheJson"] = JsonSerializer.Serialize(dtos);

                // Clean up old corrupted format
                if (settings.Values.ContainsKey("DrivesCache"))
                    settings.Values.Remove("DrivesCache");

                Helpers.DebugLogger.Log($"[MainViewModel] Saved {drives.Count} drives to cache (JSON)");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving drives to cache: {ex.Message}");
            }
        }

        private record DrivesCacheDto
        {
            public string? Name { get; init; }
            public string? Path { get; init; }
            public string? Label { get; init; }
            public long TotalSize { get; init; }
            public long AvailableFreeSpace { get; init; }
            public string? DriveFormat { get; init; }
            public string? DriveType { get; init; }
            public string? IconGlyph { get; init; }
        }

        [RelayCommand]
        public void OpenDrive(DriveItem drive)
        {
            // Switch away from Home mode if needed (same pattern as NavigateToFavorite)
            var activeViewMode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            if (activeViewMode == ViewMode.Home)
            {
                SwitchViewMode(ViewMode.MillerColumns);
            }

            var driveRoot = new FolderItem
            {
                Name = drive.Name,
                Path = drive.Path
            };

            _ = ActiveExplorer.NavigateTo(driveRoot);
        }

        private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
        {
            CanUndo = e.CanUndo;
            CanRedo = e.CanRedo;
            UndoDescription = e.UndoDescription;
            RedoDescription = e.RedoDescription;
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private async Task UndoAsync()
        {
            var result = await _operationHistory.UndoAsync();
            if (result.Success)
            {
                await RefreshCurrentFolderAsync();
                ShowToast($"Undone: {UndoDescription}");
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Undo failed");
            }
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private async Task RedoAsync()
        {
            var result = await _operationHistory.RedoAsync();
            if (result.Success)
            {
                await RefreshCurrentFolderAsync();
                ShowToast($"Redone: {RedoDescription}");
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Redo failed");
            }
        }

        public async Task ExecuteFileOperationAsync(IFileOperation operation, int? targetColumnIndex = null)
        {
            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] START - Operation: {operation.Description}, TargetColumnIndex: {targetColumnIndex}");
            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Columns: {string.Join(" > ", ActiveExplorer.Columns.Select(c => c.Name))}");

            // Copy/Move operations go through the FileOperationManager for concurrent execution
            // with pause/resume/cancel support. Other operations use the legacy synchronous path.
            if (operation is CopyFileOperation or MoveFileOperation)
            {
                await ExecuteViaConcurrentManagerAsync(operation, targetColumnIndex);
                return;
            }

            _progressViewModel.IsVisible = true;
            _progressViewModel.OperationDescription = operation.Description;

            var progress = new Progress<FileOperationProgress>(p =>
            {
                _progressViewModel.UpdateProgress(p);
            });

            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Executing operation...");
            var result = await _operationHistory.ExecuteAsync(operation, progress);

            _progressViewModel.IsVisible = false;

            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Operation result: Success={result.Success}, Error={result.ErrorMessage}");

            // Log operation to action log
            LogOperationResult(operation, result);

            if (result.Success)
            {
                // Refresh the specified column (or last column if not specified)
                Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Calling RefreshCurrentFolderAsync({targetColumnIndex})");
                await RefreshCurrentFolderAsync(targetColumnIndex);
                Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] RefreshCurrentFolderAsync completed");

                if (operation.CanUndo)
                {
                    ShowToast($"Completed: {operation.Description} — Press Ctrl+Z to undo");
                }
                else
                {
                    ShowToast($"Completed: {operation.Description}");
                }
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Operation failed");
            }

            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] ===== COMPLETE =====");
        }

        /// <summary>
        /// Starts a copy/move operation via the FileOperationManager for concurrent,
        /// pausable execution. The operation runs in the background and the UI is updated
        /// via the ActiveOperations collection.
        /// </summary>
        private async Task ExecuteViaConcurrentManagerAsync(IFileOperation operation, int? targetColumnIndex)
        {
            Helpers.DebugLogger.Log($"[ConcurrentManager] Starting: {operation.Description}");

            // Get the dispatcher queue for this thread (UI thread)
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var entry = _fileOperationManager.StartOperation(operation, dispatcherQueue);
            entry.DispatcherQueue = dispatcherQueue;

            // Subscribe to completion for this specific operation
            void OnCompleted(object? sender, OperationCompletedEventArgs e)
            {
                if (e.Entry.Id != entry.Id) return;
                _fileOperationManager.OperationCompleted -= OnCompleted;

                dispatcherQueue.TryEnqueue(async () =>
                {
                    LogOperationResult(operation, e.Result);

                    if (e.Result.Success)
                    {
                        // Add to undo history for Ctrl+Z support
                        if (operation.CanUndo)
                        {
                            await _operationHistory.ExecuteAsync(
                                new CompletedOperationWrapper(operation, e.Result),
                                null,
                                default);
                        }

                        await RefreshCurrentFolderAsync(targetColumnIndex);
                        ShowToast($"Completed: {operation.Description}");
                    }
                    else if (e.Entry.Status != Services.OperationStatus.Cancelled)
                    {
                        ShowError(e.Result.ErrorMessage ?? "Operation failed");
                    }
                });
            }

            _fileOperationManager.OperationCompleted += OnCompleted;

            // Don't await the background task - the operation runs concurrently
            Helpers.DebugLogger.Log($"[ConcurrentManager] Operation started in background: ID={entry.Id}");
        }

        private void LogOperationResult(IFileOperation operation, OperationResult result)
        {
            _actionLogService.LogOperation(new Models.ActionLogEntry
            {
                OperationType = operation switch
                {
                    CopyFileOperation => "Copy",
                    MoveFileOperation => "Move",
                    DeleteFileOperation => "Delete",
                    RenameFileOperation => "Rename",
                    _ => operation.GetType().Name.Replace("Operation", "")
                },
                Description = operation.Description,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                SourcePaths = result.AffectedPaths,
                ItemCount = result.AffectedPaths.Count
            });
        }

        private async Task RefreshCurrentFolderAsync(int? columnIndex = null, ExplorerViewModel? explorer = null)
        {
            explorer ??= ActiveExplorer;
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] START - columnIndex: {columnIndex}");

            if (explorer?.Columns == null || explorer.Columns.Count == 0)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] No columns to refresh - ABORT");
                return;
            }

            // Determine which column to refresh
            // If columnIndex is provided, use it; otherwise refresh the last column
            int targetIndex = columnIndex ?? explorer.Columns.Count - 1;
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Target index: {targetIndex} (total columns: {explorer.Columns.Count})");

            // Validate index
            if (targetIndex < 0 || targetIndex >= explorer.Columns.Count)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Invalid index - ABORT");
                return;
            }

            var targetColumn = explorer.Columns[targetIndex];
            var savedName = targetColumn.SelectedChild?.Name;

            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Refreshing column '{targetColumn.Name}' (saved selection: {savedName ?? "null"})");
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Children before reload: {targetColumn.Children.Count}");

            // CRITICAL: Clear selection BEFORE reload to prevent stale reference
            targetColumn.SelectedChild = null;

            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Calling ReloadAsync()...");
            await targetColumn.ReloadAsync();
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] ReloadAsync() completed. Children after reload: {targetColumn.Children.Count}");

            // Restore previous selection by name
            if (savedName != null)
            {
                var restored = targetColumn.Children.FirstOrDefault(c =>
                    c.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase));
                targetColumn.SelectedChild = restored; // null if not found (selection cleared)
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Restored selection: {restored?.Name ?? "null"}");
            }
            else
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] No selection to restore");
            }

            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] ===== COMPLETE =====");
        }

        public void ShowToast(string message, int durationMs = 3000)
        {
            _toastTimer?.Dispose();
            ToastMessage = message;
            IsToastVisible = true;

            _toastTimer = new System.Threading.Timer(_ =>
            {
                IsToastVisible = false;
            }, null, durationMs, System.Threading.Timeout.Infinite);
        }

        private void ShowError(string message)
        {
            ShowToast($"Error: {message}", 5000);
        }

        #region Favorites

        private void LoadFavorites()
        {
            try
            {
                var items = _favoritesService.LoadFavorites();
                Favorites.Clear();
                foreach (var item in items)
                {
                    Favorites.Add(item);
                }
                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {items.Count} favorites");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading favorites: {ex.Message}");
            }
        }

        public void AddToFavorites(string path)
        {
            if (Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return;

            var updated = _favoritesService.AddFavorite(path, Favorites.ToList());
            _favoritesService.SaveFavorites(updated);

            Favorites.Clear();
            foreach (var item in updated)
            {
                Favorites.Add(item);
            }
            Helpers.DebugLogger.Log($"[MainViewModel] Added to favorites: {path}");
        }

        public void RemoveFromFavorites(string path)
        {
            var updated = _favoritesService.RemoveFavorite(path, Favorites.ToList());
            _favoritesService.SaveFavorites(updated);

            Favorites.Clear();
            foreach (var item in updated)
            {
                Favorites.Add(item);
            }
            Helpers.DebugLogger.Log($"[MainViewModel] Removed from favorites: {path}");
        }

        public bool IsFavorite(string path)
        {
            return Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        public void NavigateToFavorite(FavoriteItem favorite)
        {
            if (!System.IO.Directory.Exists(favorite.Path))
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Favorite path not found: {favorite.Path}");
                return;
            }

            // Switch away from Home mode if needed
            var activeViewMode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            if (activeViewMode == ViewMode.Home)
            {
                SwitchViewMode(ViewMode.MillerColumns);
            }

            var folder = new FolderItem { Name = favorite.Name, Path = favorite.Path };
            _ = ActiveExplorer.NavigateTo(folder);
            Helpers.DebugLogger.Log($"[MainViewModel] Navigated to favorite: {favorite.Name}");
        }

        #endregion

        #region Recent Folders

        private void LoadRecentFolders()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values["RecentFoldersJson"] is string json && !string.IsNullOrEmpty(json))
                {
                    var dtos = JsonSerializer.Deserialize<List<RecentFolderDto>>(json);
                    if (dtos != null)
                    {
                        RecentFolders.Clear();
                        for (int i = 0; i < dtos.Count; i++)
                        {
                            if (!string.IsNullOrEmpty(dtos[i].Path))
                            {
                                RecentFolders.Add(new FavoriteItem
                                {
                                    Name = dtos[i].Name ?? "",
                                    Path = dtos[i].Path!,
                                    IconGlyph = Services.IconService.Current?.FolderGlyph ?? "\uED53",
                                    IconColor = "#A0A0A0",
                                    Order = i
                                });
                            }
                        }
                        Helpers.DebugLogger.Log($"[MainViewModel] Loaded {RecentFolders.Count} recent folders");
                    }
                }

                // Clean up old CompositeValue format
                if (settings.Values.ContainsKey("RecentFolders"))
                    settings.Values.Remove("RecentFolders");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading recent folders: {ex.Message}");
            }
        }

        private void SaveRecentFolders()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var dtos = RecentFolders.Select(r => new RecentFolderDto { Name = r.Name, Path = r.Path }).ToList();
                settings.Values["RecentFoldersJson"] = JsonSerializer.Serialize(dtos);

                // Clean up old format
                if (settings.Values.ContainsKey("RecentFolders"))
                    settings.Values.Remove("RecentFolders");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving recent folders: {ex.Message}");
            }
        }

        private record RecentFolderDto
        {
            public string? Name { get; init; }
            public string? Path { get; init; }
        }

        private void AddRecentFolder(string path)
        {
            // Skip drive roots and virtual paths
            if (path == "PC" || path.Length <= 3) return;

            // Remove if already exists (will re-add at top)
            var existing = RecentFolders.FirstOrDefault(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                RecentFolders.Remove(existing);
            }

            // Add at top
            RecentFolders.Insert(0, new FavoriteItem
            {
                Name = System.IO.Path.GetFileName(path),
                Path = path,
                IconGlyph = Services.IconService.Current?.FolderGlyph ?? "\uED53",
                IconColor = "#A0A0A0",
                Order = 0
            });

            // Trim to max
            while (RecentFolders.Count > MaxRecentFolders)
            {
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            }

            // SaveRecentFolders() 제거 — Cleanup()에서 일괄 저장
        }

        #endregion

        #region Saved Connections

        private async void LoadSavedConnections()
        {
            try
            {
                var connectionService = App.Current.Services.GetRequiredService<Services.ConnectionManagerService>();
                await connectionService.LoadConnectionsAsync();

                SavedConnections.Clear();
                foreach (var conn in connectionService.SavedConnections)
                    SavedConnections.Add(conn);

                RebuildAllDrives();

                // Sync changes from service
                connectionService.SavedConnections.CollectionChanged += (s, e) =>
                {
                    SavedConnections.Clear();
                    foreach (var c in connectionService.SavedConnections)
                        SavedConnections.Add(c);
                    RebuildAllDrives();
                };

                Helpers.DebugLogger.Log($"[MainViewModel] {SavedConnections.Count}개의 저장된 연결 로드");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] 저장된 연결 로드 오류: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Settings에서 복귀할 이전 ViewMode
        /// </summary>
        private ViewMode _preSettingsViewMode = ViewMode.MillerColumns;
        private ViewMode _preSettingsLeftViewMode = ViewMode.MillerColumns;

        /// <summary>
        /// Settings 모드에서 이전 뷰로 복귀
        /// </summary>
        public void ExitSettings()
        {
            if (CurrentViewMode != ViewMode.Settings) return;

            CurrentViewMode = _preSettingsViewMode;
            LeftViewMode = _preSettingsLeftViewMode;
            Helpers.DebugLogger.Log($"[MainViewModel] Settings → {Helpers.ViewModeExtensions.GetDisplayName(_preSettingsViewMode)} 복귀");
            UpdateStatusBar();
        }

        /// <summary>
        /// 뷰 모드 전환 — 활성 패널에 적용
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            // Settings mode always targets the left pane
            if (mode == ViewMode.Settings)
            {
                if (CurrentViewMode == ViewMode.Settings) return;
                _preSettingsViewMode = CurrentViewMode;
                _preSettingsLeftViewMode = LeftViewMode;
                ActivePane = ActivePane.Left;
                CurrentViewMode = ViewMode.Settings;
                LeftViewMode = ViewMode.Settings;
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: Settings (always left pane)");
                UpdateStatusBar();
                return;
            }

            // Home mode always targets the left pane (HomeView only exists in left pane)
            if (mode == ViewMode.Home)
            {
                if (CurrentViewMode == ViewMode.Home) return;
                ActivePane = ActivePane.Left;
                CurrentViewMode = ViewMode.Home;
                LeftViewMode = ViewMode.Home;
                SaveViewModePreference();
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: Home (always left pane)");
                UpdateStatusBar();
                return;
            }

            // Determine which pane's view mode to update
            if (IsSplitViewEnabled && ActivePane == ActivePane.Right)
            {
                if (RightViewMode == mode) return;

                if (Helpers.ViewModeExtensions.IsIconMode(mode))
                {
                    CurrentIconSize = mode;
                    RightViewMode = mode;
                }
                else
                {
                    RightViewMode = mode;
                }

                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(mode);
                Helpers.DebugLogger.Log($"[MainViewModel] Right pane AutoNav: {RightExplorer.EnableAutoNavigation} (mode: {mode})");
            }
            else
            {
                if (CurrentViewMode == mode) return;

                if (Helpers.ViewModeExtensions.IsIconMode(mode))
                {
                    CurrentIconSize = mode;
                    CurrentViewMode = mode;
                    LeftViewMode = mode;
                }
                else
                {
                    CurrentViewMode = mode;
                    LeftViewMode = mode;
                }

                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(mode);
                Helpers.DebugLogger.Log($"[MainViewModel] Left pane AutoNav: {LeftExplorer.EnableAutoNavigation} (mode: {mode})");
            }

            SaveViewModePreference();
            UpdateActiveTabHeader();
            // 활성 탭의 ViewMode도 즉시 동기화
            if (ActiveTab != null)
            {
                ActiveTab.ViewMode = CurrentViewMode;
                ActiveTab.IconSize = CurrentIconSize;
            }
            Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: {Helpers.ViewModeExtensions.GetDisplayName(mode)}");
            UpdateStatusBar();
        }

        /// <summary>
        /// Determines if auto-navigation should be enabled based on view mode and MillerClickBehavior setting.
        /// </summary>
        private bool ShouldAutoNavigate(ViewMode mode)
        {
            if (mode != ViewMode.MillerColumns) return false;
            try
            {
                var settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
                return settings.MillerClickBehavior != "double";
            }
            catch { return true; }
        }

        /// <summary>
        /// ViewMode 설정 저장 (LocalSettings)
        /// </summary>
        private void SaveViewModePreference()
        {
            try
            {
                // Don't persist Home or Settings as startup mode
                if (CurrentViewMode == ViewMode.Home || CurrentViewMode == ViewMode.Settings) return;

                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["ViewMode"] = (int)CurrentViewMode;
                settings.Values["IconSize"] = (int)CurrentIconSize;
                settings.Values["LeftViewMode"] = (int)LeftViewMode;
                settings.Values["RightViewMode"] = (int)RightViewMode;
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode saved: L={LeftViewMode}, R={RightViewMode}, IconSize={CurrentIconSize}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveViewModePreference error: {ex.Message}");
            }
        }

        /// <summary>
        /// ViewMode 설정 로드 (앱 시작 시)
        /// </summary>
        public void LoadViewModePreference()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ViewMode", out var mode) && mode is int modeInt
                    && System.Enum.IsDefined(typeof(ViewMode), modeInt))
                {
                    CurrentViewMode = (ViewMode)modeInt;
                    LeftViewMode = CurrentViewMode;
                }

                if (settings.Values.TryGetValue("IconSize", out var size) && size is int sizeInt
                    && System.Enum.IsDefined(typeof(ViewMode), sizeInt))
                {
                    CurrentIconSize = (ViewMode)sizeInt;
                }

                if (settings.Values.TryGetValue("LeftViewMode", out var leftMode) && leftMode is int leftInt
                    && System.Enum.IsDefined(typeof(ViewMode), leftInt))
                {
                    LeftViewMode = (ViewMode)leftInt;
                    CurrentViewMode = LeftViewMode;
                }

                if (settings.Values.TryGetValue("RightViewMode", out var rightMode) && rightMode is int rightInt
                    && System.Enum.IsDefined(typeof(ViewMode), rightInt))
                {
                    RightViewMode = (ViewMode)rightInt;
                }

                // Load split view state
                if (settings.Values.TryGetValue("IsSplitViewEnabled", out var splitEnabled))
                {
                    IsSplitViewEnabled = (bool)splitEnabled;
                }

                // Load preview state
                if (settings.Values.TryGetValue("IsLeftPreviewEnabled", out var leftPrev))
                    IsLeftPreviewEnabled = (bool)leftPrev;
                if (settings.Values.TryGetValue("IsRightPreviewEnabled", out var rightPrev))
                    IsRightPreviewEnabled = (bool)rightPrev;

                // Set auto-navigation based on loaded view mode
                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(LeftViewMode);
                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(RightViewMode);
                Helpers.DebugLogger.Log($"[MainViewModel] AutoNav: L={LeftExplorer.EnableAutoNavigation}, R={RightExplorer.EnableAutoNavigation}");

                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode loaded: L={Helpers.ViewModeExtensions.GetDisplayName(LeftViewMode)}, R={Helpers.ViewModeExtensions.GetDisplayName(RightViewMode)}, Split={IsSplitViewEnabled}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadViewModePreference error: {ex.Message}");
                CurrentViewMode = ViewMode.MillerColumns;
                LeftViewMode = ViewMode.MillerColumns;
                RightViewMode = ViewMode.MillerColumns;
                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.MillerColumns);
                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.MillerColumns);
            }
        }

        /// <summary>
        /// Toggle preview panel for the active pane.
        /// </summary>
        public void TogglePreview()
        {
            if (ActivePane == ActivePane.Left)
                IsLeftPreviewEnabled = !IsLeftPreviewEnabled;
            else
                IsRightPreviewEnabled = !IsRightPreviewEnabled;

            SavePreviewState();
        }

        /// <summary>
        /// Save preview panel state to LocalSettings.
        /// </summary>
        public void SavePreviewState()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["IsLeftPreviewEnabled"] = IsLeftPreviewEnabled;
                settings.Values["IsRightPreviewEnabled"] = IsRightPreviewEnabled;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving preview state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save preview panel widths (called from MainWindow on close).
        /// </summary>
        public void SavePreviewWidths(double leftWidth, double rightWidth)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["LeftPreviewWidth"] = leftWidth;
                settings.Values["RightPreviewWidth"] = rightWidth;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving preview widths: {ex.Message}");
            }
        }

        /// <summary>
        /// Save split view state to LocalSettings
        /// </summary>
        private void SaveSplitViewState()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["IsSplitViewEnabled"] = IsSplitViewEnabled;

                // Save right pane path for restore on next launch
                if (!string.IsNullOrEmpty(RightExplorer?.CurrentPath) && RightExplorer.CurrentPath != "PC")
                {
                    settings.Values["RightPanePath"] = RightExplorer.CurrentPath;
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Split state saved: {IsSplitViewEnabled}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving split state: {ex.Message}");
            }
        }

        #region Tab Management

        /// <summary>
        /// Add a new Home tab and switch to it.
        /// </summary>
        public void AddNewTab()
        {
            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.Home);

            var tab = new TabItem
            {
                Header = "Home",
                Path = "",
                ViewMode = ViewMode.Home,
                IconSize = ViewMode.IconMedium,
                IsActive = false,
                Explorer = explorer
            };
            Tabs.Add(tab);
            SwitchToTab(Tabs.Count - 1);
            Helpers.DebugLogger.Log($"[MainViewModel] New tab added (total: {Tabs.Count})");
        }

        /// <summary>
        /// Switch to a tab by index. Saves old tab state, restores new tab state.
        /// Minimizes PropertyChanged events: backing fields are set directly,
        /// and the caller (code-behind) is responsible for updating UI manually.
        /// </summary>
        public void SwitchToTab(int index)
        {
            if (index < 0 || index >= Tabs.Count)
                return;
            if (index == ActiveTabIndex && Tabs[index].IsActive)
                return;

            IsSwitchingTab = true;
            try
            {
                // 현재 탭 상태 동기화 (Path, ViewMode만)
                SaveActiveTabState();

                // Deactivate old tab
                if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
                    Tabs[ActiveTabIndex].IsActive = false;

                // Activate new tab — backing field 직접 설정으로 PropertyChanged 방지
                _activeTabIndex = index;
                Tabs[index].IsActive = true;
                OnPropertyChanged(nameof(ActiveTab));

                // Explorer가 없으면 생성, 있지만 경로가 미로드이면 탐색 실행
                if (Tabs[index].Explorer == null)
                {
                    InitializeTabExplorer(Tabs[index]);
                }
                else if (!string.IsNullOrEmpty(Tabs[index].Path)
                    && Tabs[index].ViewMode != ViewMode.Home
                    && string.IsNullOrEmpty(Tabs[index].Explorer.CurrentPath))
                {
                    // H4: 비활성 탭에서 지연된 NavigateToPath 실행
                    LoadDeferredTabPath(Tabs[index]);
                }

                // ★ LeftExplorer 필드 직접 설정 — PropertyChanged 미발생 (SetProperty 우회)
                var old = _leftExplorer;
                if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
                _leftExplorer = Tabs[index].Explorer!;
                if (_leftExplorer != null) _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;

                // ★ ViewMode도 backing field 직접 설정 — PropertyChanged 미발생
                _currentViewMode = Tabs[index].ViewMode;
                _leftViewMode = Tabs[index].ViewMode;
                if (Helpers.ViewModeExtensions.IsIconMode(Tabs[index].ViewMode))
                    _currentIconSize = Tabs[index].IconSize;
                _leftExplorer.EnableAutoNavigation = ShouldAutoNavigate(Tabs[index].ViewMode);

                Helpers.DebugLogger.Log($"[MainViewModel] Switched to tab {index}: {Tabs[index].Header}");
                UpdateStatusBar();
                SyncNavigationHistoryState();
            }
            finally
            {
                IsSwitchingTab = false;
            }
        }

        /// <summary>
        /// Close a tab by index. Blocks if it's the last tab.
        /// </summary>
        public void CloseTab(int index)
        {
            if (Tabs.Count <= 1) return; // Don't close the last tab
            if (index < 0 || index >= Tabs.Count) return;

            bool wasActive = (index == ActiveTabIndex);
            // 닫히는 탭의 Explorer 정리
            Tabs[index].Explorer?.Cleanup();
            Tabs.RemoveAt(index);

            if (wasActive)
            {
                // Switch to closest valid tab
                int newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTabIndex = -1; // Force switch
                SwitchToTab(newIndex);
            }
            else if (index < ActiveTabIndex)
            {
                // Active tab shifted left
                ActiveTabIndex--;
                OnPropertyChanged(nameof(ActiveTab));
            }

            Helpers.DebugLogger.Log($"[MainViewModel] Closed tab {index} (remaining: {Tabs.Count})");
        }

        /// <summary>
        /// Close all tabs except the specified one.
        /// Returns list of closed tab IDs so the caller can clean up panels.
        /// </summary>
        public List<string> CloseOtherTabs(TabItem keepTab)
        {
            var closedIds = new List<string>();
            // Close from right to left to maintain indices
            for (int i = Tabs.Count - 1; i >= 0; i--)
            {
                if (Tabs[i] == keepTab) continue;
                closedIds.Add(Tabs[i].Id);
                Tabs[i].Explorer?.Cleanup();
                Tabs.RemoveAt(i);
            }

            int newIndex = Tabs.IndexOf(keepTab);
            ActiveTabIndex = -1; // Force switch
            SwitchToTab(newIndex);
            Helpers.DebugLogger.Log($"[MainViewModel] Closed other tabs, remaining: {Tabs.Count}");
            return closedIds;
        }

        /// <summary>
        /// Close all tabs to the right of the specified tab.
        /// Returns list of closed tab IDs so the caller can clean up panels.
        /// </summary>
        public List<string> CloseTabsToRight(TabItem tab)
        {
            int tabIndex = Tabs.IndexOf(tab);
            if (tabIndex < 0) return new List<string>();

            var closedIds = new List<string>();
            for (int i = Tabs.Count - 1; i > tabIndex; i--)
            {
                closedIds.Add(Tabs[i].Id);
                Tabs[i].Explorer?.Cleanup();
                Tabs.RemoveAt(i);
            }

            // If active tab was removed, switch to the kept tab
            if (ActiveTabIndex > tabIndex)
            {
                ActiveTabIndex = -1;
                SwitchToTab(tabIndex);
            }

            Helpers.DebugLogger.Log($"[MainViewModel] Closed tabs to right of {tabIndex}, remaining: {Tabs.Count}");
            return closedIds;
        }

        /// <summary>
        /// Duplicate a tab: create a new tab with the same path, view mode, and icon size.
        /// Insert it right after the source tab.
        /// </summary>
        public TabItem DuplicateTab(TabItem sourceTab)
        {
            SaveActiveTabState();

            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(sourceTab.ViewMode);

            var newTab = new TabItem
            {
                Header = sourceTab.Header,
                Path = sourceTab.Path,
                ViewMode = sourceTab.ViewMode,
                IconSize = sourceTab.IconSize,
                IsActive = false,
                Explorer = explorer
            };

            int insertIndex = Tabs.IndexOf(sourceTab) + 1;
            Tabs.Insert(insertIndex, newTab);

            // Navigate the new explorer to the source path
            if (!string.IsNullOrEmpty(sourceTab.Path) && sourceTab.ViewMode != ViewMode.Home)
            {
                _ = explorer.NavigateToPath(sourceTab.Path);
            }

            SwitchToTab(insertIndex);
            Helpers.DebugLogger.Log($"[MainViewModel] Duplicated tab '{sourceTab.Header}' at index {insertIndex}");
            return newTab;
        }

        /// <summary>
        /// Copy current explorer state into the active tab.
        /// </summary>
        public void SaveActiveTabState()
        {
            var tab = ActiveTab;
            if (tab == null) return;

            if (tab.ViewMode != CurrentViewMode)
                tab.ViewMode = CurrentViewMode;
            if (tab.IconSize != CurrentIconSize)
                tab.IconSize = CurrentIconSize;
            tab.Path = tab.Explorer?.CurrentPath ?? "";
        }

        /// <summary>
        /// 탭에 ExplorerViewModel을 최초 생성 (앱 시작/세션 복원 시).
        /// 이미 Explorer가 있으면 아무것도 하지 않음.
        /// </summary>
        private async void InitializeTabExplorer(TabItem tab)
        {
            if (tab.Explorer != null) return;

            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
            tab.Explorer = explorer;

            if (!string.IsNullOrEmpty(tab.Path) && tab.ViewMode != ViewMode.Home)
            {
                try
                {
                    if (System.IO.Directory.Exists(tab.Path))
                    {
                        await explorer.NavigateToPath(tab.Path);
                    }
                    else
                    {
                        tab.Path = "";
                        tab.ViewMode = ViewMode.Home;
                        Helpers.DebugLogger.Log($"[MainViewModel] Tab path not found, falling back to Home");
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainViewModel] InitializeTabExplorer error: {ex.Message}");
                    tab.Path = "";
                    tab.ViewMode = ViewMode.Home;
                }
            }
        }

        /// <summary>
        /// H4: 비활성 탭의 지연된 NavigateToPath 실행 (최초 전환 시)
        /// </summary>
        private async void LoadDeferredTabPath(TabItem tab)
        {
            if (tab.Explorer == null || string.IsNullOrEmpty(tab.Path)) return;

            try
            {
                if (System.IO.Directory.Exists(tab.Path))
                {
                    await tab.Explorer.NavigateToPath(tab.Path);
                }
                else
                {
                    tab.Path = "";
                    tab.ViewMode = ViewMode.Home;
                    Helpers.DebugLogger.Log($"[MainViewModel] Deferred tab path not found, falling back to Home");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] LoadDeferredTabPath error: {ex.Message}");
                tab.Path = "";
                tab.ViewMode = ViewMode.Home;
            }
        }

        /// <summary>
        /// SwitchToTab에서 PropertyChanged를 우회한 후, XAML x:Bind가 필요로 하는
        /// 최소한의 PropertyChanged만 일괄 발생시킨다.
        /// code-behind에서 ResubscribeLeftExplorer() 호출 후 사용.
        /// Explorer/ActiveExplorer는 ResubscribeLeftExplorer가 이미 처리하므로 제외.
        /// </summary>
        public void NotifyViewModeChanged()
        {
            // LeftViewMode는 XAML x:Bind에서 사용하지 않으므로 제거 (불필요한 바인딩 평가 방지)
            OnPropertyChanged(nameof(CurrentViewMode));
        }

        /// <summary>
        /// Sync the active tab's header/icon with the current explorer state.
        /// </summary>
        public void UpdateActiveTabHeader()
        {
            var tab = ActiveTab;
            if (tab == null) return;

            if (CurrentViewMode == ViewMode.Home)
            {
                tab.Header = "Home";
                tab.ViewMode = ViewMode.Home;
            }
            else
            {
                tab.Header = tab.Explorer?.CurrentFolderName ?? "Home";
                tab.ViewMode = CurrentViewMode;
            }
        }

        /// <summary>
        /// Save all tab states to settings (JSON persistence).
        /// </summary>
        public void SaveTabsToSettings()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                var dtos = Tabs.Select(t => new TabStateDto(
                    t.Id, t.Header, t.Path, (int)t.ViewMode, (int)t.IconSize
                )).ToList();

                settings.TabsJson = JsonSerializer.Serialize(dtos);
                settings.ActiveTabIndex = ActiveTabIndex;
                Helpers.DebugLogger.Log($"[MainViewModel] Saved {dtos.Count} tabs to settings");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving tabs: {ex.Message}");
            }
        }

        /// <summary>
        /// Load tab states from settings. Replaces current tabs.
        /// </summary>
        public void LoadTabsFromSettings()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                var json = settings.TabsJson;

                if (string.IsNullOrEmpty(json))
                {
                    // 저장된 탭 없음 — 기본 탭 유지, Explorer 할당 확인
                    if (Tabs.Count > 0)
                    {
                        if (Tabs[0].Explorer == null)
                            Tabs[0].Explorer = LeftExplorer;
                        Tabs[0].IsActive = true;
                        ActiveTabIndex = 0;
                        OnPropertyChanged(nameof(ActiveTab));
                    }
                    return;
                }

                var dtos = JsonSerializer.Deserialize<List<TabStateDto>>(json);
                if (dtos == null || dtos.Count == 0)
                {
                    EnsureDefaultTab();
                    Tabs[0].Explorer = LeftExplorer;
                    return;
                }

                Tabs.Clear();
                int savedIndex = Math.Clamp(settings.ActiveTabIndex, 0, dtos.Count - 1);

                for (int i = 0; i < dtos.Count; i++)
                {
                    var dto = dtos[i];
                    var tabViewMode = System.Enum.IsDefined(typeof(ViewMode), dto.ViewMode)
                        ? (ViewMode)dto.ViewMode : ViewMode.MillerColumns;
                    var tabIconSize = System.Enum.IsDefined(typeof(ViewMode), dto.IconSize)
                        ? (ViewMode)dto.IconSize : ViewMode.IconMedium;

                    var tab = new TabItem
                    {
                        Id = dto.Id,
                        Header = dto.Header,
                        Path = dto.Path,
                        ViewMode = tabViewMode,
                        IconSize = tabIconSize,
                        IsActive = false
                    };

                    // 활성 탭은 기존 LeftExplorer 재활용
                    if (i == savedIndex)
                    {
                        tab.Explorer = LeftExplorer;
                    }
                    else
                    {
                        // 비활성 탭은 ExplorerViewModel만 생성하고 NavigateToPath는 호출하지 않음
                        // Path는 tab.Path에 저장되어 있으므로 최초 전환 시 InitializeTabExplorer에서 로드
                        var root = new FolderItem { Name = "PC", Path = "PC" };
                        var explorer = new ExplorerViewModel(root, _fileService);
                        explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
                        tab.Explorer = explorer;
                    }

                    Tabs.Add(tab);
                }

                ActiveTabIndex = -1; // Force switch
                SwitchToTab(savedIndex);

                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {Tabs.Count} tabs from settings (active: {savedIndex})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading tabs: {ex.Message}");
                EnsureDefaultTab();
                Tabs[0].Explorer = LeftExplorer;
            }
        }

        /// <summary>
        /// Load a single tab from a tear-off DTO. Replaces all existing tabs.
        /// Used when creating a new window from a torn-off tab.
        /// </summary>
        public async void LoadSingleTabFromDto(TabStateDto dto)
        {
            try
            {
                Tabs.Clear();

                var tearViewMode = System.Enum.IsDefined(typeof(ViewMode), dto.ViewMode)
                    ? (ViewMode)dto.ViewMode : ViewMode.MillerColumns;
                var tearIconSize = System.Enum.IsDefined(typeof(ViewMode), dto.IconSize)
                    ? (ViewMode)dto.IconSize : ViewMode.IconMedium;

                var tab = new TabItem
                {
                    Id = dto.Id,
                    Header = dto.Header,
                    Path = dto.Path,
                    ViewMode = tearViewMode,
                    IconSize = tearIconSize,
                    IsActive = true
                };

                // Create explorer and assign
                var root = new FolderItem { Name = "PC", Path = "PC" };
                var explorer = new ExplorerViewModel(root, _fileService);
                explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
                tab.Explorer = explorer;

                Tabs.Add(tab);

                // Set LeftExplorer directly
                var old = _leftExplorer;
                if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
                _leftExplorer = explorer;
                _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;

                _activeTabIndex = 0;
                _currentViewMode = tab.ViewMode;
                _leftViewMode = tab.ViewMode;
                if (Helpers.ViewModeExtensions.IsIconMode(tab.ViewMode))
                    _currentIconSize = tab.IconSize;

                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(Explorer));
                OnPropertyChanged(nameof(ActiveExplorer));
                OnPropertyChanged(nameof(CurrentViewMode));

                // Navigate to path if not Home
                if (tab.ViewMode != ViewMode.Home && !string.IsNullOrEmpty(tab.Path))
                {
                    await explorer.NavigateToPath(tab.Path);
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Loaded tear-off tab: {tab.Header} @ {tab.Path}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading tear-off tab: {ex.Message}");
                EnsureDefaultTab();
                Tabs[0].Explorer = LeftExplorer;
            }
        }

        #endregion

        /// <summary>
        /// Get the path to navigate the right pane to when split view is activated.
        /// Tries: saved right pane path → first available drive → user profile folder.
        /// </summary>
        public string GetRightPaneInitialPath()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("RightPanePath", out var savedPath) && savedPath is string path)
                {
                    if (System.IO.Directory.Exists(path))
                        return path;
                }
            }
            catch { }

            // Fallback: first available drive
            if (Drives.Count > 0)
            {
                return Drives[0].Path;
            }

            // Last resort: user profile
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }
}
