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

        // Engine — Split View (Dual-Pane)
        private ExplorerViewModel _leftExplorer;
        public ExplorerViewModel LeftExplorer
        {
            get => _leftExplorer;
            set => SetProperty(ref _leftExplorer, value);
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

        [ObservableProperty]
        private string _statusBarText = string.Empty;

        [ObservableProperty]
        private string _toastMessage = string.Empty;

        [ObservableProperty]
        private bool _isToastVisible = false;

        private System.Threading.Timer? _toastTimer;

        [ObservableProperty]
        private ViewMode _currentViewMode = ViewMode.MillerColumns;

        [ObservableProperty]
        private ViewMode _currentIconSize = ViewMode.IconMedium; // Icon 모드 기본 크기

        public FileOperationProgressViewModel ProgressViewModel => _progressViewModel;

        public MainViewModel(FileSystemService fileService, FavoritesService favoritesService, ActionLogService actionLogService)
        {
            _fileService = fileService;
            _favoritesService = favoritesService;
            _actionLogService = actionLogService;
            _operationHistory = new FileOperationHistory();
            _progressViewModel = new FileOperationProgressViewModel();

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

        private void Initialize()
        {
            // Dummy tabs
            Tabs.Add(new TabItem { Header = "Project Span", Icon = "\uEA34" }); // ri-apps-2-fill

            // Initialize Engines with a conceptual Root
            var root = new FolderItem { Name = "PC", Path = "PC" };
            LeftExplorer = new ExplorerViewModel(root, _fileService);

            var rightRoot = new FolderItem { Name = "PC", Path = "PC" };
            RightExplorer = new ExplorerViewModel(rightRoot, _fileService);

            // Populate Sidebar
            LoadDrives();
            LoadFavorites();
            LoadRecentFolders();

            // Load ViewMode preference (includes split state)
            LoadViewModePreference();

            // Track navigation for recent folders (both panes)
            LeftExplorer.PropertyChanged += (s, e) =>
            {
                if (_isCleaningUp) return;
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(LeftExplorer.CurrentPath))
                {
                    AddRecentFolder(LeftExplorer.CurrentPath);
                }
            };
            RightExplorer.PropertyChanged += (s, e) =>
            {
                if (_isCleaningUp) return;
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(RightExplorer.CurrentPath))
                {
                    AddRecentFolder(RightExplorer.CurrentPath);
                }
            };
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

                // Clear collections (safe now - _isCleaningUp suppresses side effects)
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
            var drives = new List<DriveItem>();

            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values["DrivesCache"] is Windows.Storage.ApplicationDataCompositeValue composite)
                {
                    int count = (int)(composite["Count"] ?? 0);

                    for (int i = 0; i < count; i++)
                    {
                        var driveKey = $"Drive{i}";
                        if (composite[driveKey] is Windows.Storage.ApplicationDataCompositeValue driveData)
                        {
                            var drive = new DriveItem
                            {
                                Name = driveData["Name"] as string ?? "",
                                Path = driveData["Path"] as string ?? "",
                                Label = driveData["Label"] as string ?? "",
                                TotalSize = (long)(driveData["TotalSize"] ?? 0L),
                                AvailableFreeSpace = (long)(driveData["AvailableFreeSpace"] ?? 0L),
                                DriveFormat = driveData["DriveFormat"] as string ?? "",
                                DriveType = driveData["DriveType"] as string ?? "",
                                IconGlyph = driveData["IconGlyph"] as string ?? "\uEEA1"
                            };
                            drives.Add(drive);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading drives from cache: {ex.Message}");
            }

            return drives;
        }

        /// <summary>
        /// Save drives to LocalSettings cache
        /// </summary>
        private void SaveDrivesCache(List<DriveItem> drives)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var composite = new Windows.Storage.ApplicationDataCompositeValue
                {
                    ["Count"] = drives.Count
                };

                for (int i = 0; i < drives.Count; i++)
                {
                    var drive = drives[i];
                    var driveData = new Windows.Storage.ApplicationDataCompositeValue
                    {
                        ["Name"] = drive.Name,
                        ["Path"] = drive.Path,
                        ["Label"] = drive.Label,
                        ["TotalSize"] = drive.TotalSize,
                        ["AvailableFreeSpace"] = drive.AvailableFreeSpace,
                        ["DriveFormat"] = drive.DriveFormat,
                        ["DriveType"] = drive.DriveType,
                        ["IconGlyph"] = drive.IconGlyph
                    };
                    composite[$"Drive{i}"] = driveData;
                }

                settings.Values["DrivesCache"] = composite;
                Helpers.DebugLogger.Log($"[MainViewModel] Saved {drives.Count} drives to cache");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving drives to cache: {ex.Message}");
            }
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

            ActiveExplorer.NavigateTo(driveRoot);
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
            ActiveExplorer.NavigateTo(folder);
            Helpers.DebugLogger.Log($"[MainViewModel] Navigated to favorite: {favorite.Name}");
        }

        #endregion

        #region Recent Folders

        private void LoadRecentFolders()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values["RecentFolders"] is Windows.Storage.ApplicationDataCompositeValue composite)
                {
                    int count = (int)(composite["Count"] ?? 0);
                    RecentFolders.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        var name = composite[$"N{i}"] as string ?? "";
                        var path = composite[$"P{i}"] as string ?? "";
                        if (!string.IsNullOrEmpty(path))
                        {
                            RecentFolders.Add(new FavoriteItem
                            {
                                Name = name,
                                Path = path,
                                IconGlyph = "\uED93",
                                IconColor = "#A0A0A0",
                                Order = i
                            });
                        }
                    }
                    Helpers.DebugLogger.Log($"[MainViewModel] Loaded {RecentFolders.Count} recent folders");
                }
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
                var composite = new Windows.Storage.ApplicationDataCompositeValue
                {
                    ["Count"] = RecentFolders.Count
                };
                for (int i = 0; i < RecentFolders.Count; i++)
                {
                    composite[$"N{i}"] = RecentFolders[i].Name;
                    composite[$"P{i}"] = RecentFolders[i].Path;
                }
                settings.Values["RecentFolders"] = composite;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving recent folders: {ex.Message}");
            }
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
                IconGlyph = "\uED93",
                IconColor = "#A0A0A0",
                Order = 0
            });

            // Trim to max
            while (RecentFolders.Count > MaxRecentFolders)
            {
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            }

            SaveRecentFolders();
        }

        #endregion

        /// <summary>
        /// 뷰 모드 전환 — 활성 패널에 적용
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            // Home mode always targets the left pane (HomeView only exists in left pane)
            if (mode == ViewMode.Home)
            {
                if (CurrentViewMode == ViewMode.Home) return;
                ActivePane = ActivePane.Left;
                CurrentViewMode = ViewMode.Home;
                LeftViewMode = ViewMode.Home;
                SaveViewModePreference();
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: Home (always left pane)");
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
            Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: {Helpers.ViewModeExtensions.GetDisplayName(mode)}");
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
                // Don't persist Home as startup mode
                if (CurrentViewMode == ViewMode.Home) return;

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

                if (settings.Values.TryGetValue("ViewMode", out var mode))
                {
                    CurrentViewMode = (ViewMode)(int)mode;
                    LeftViewMode = CurrentViewMode;
                }

                if (settings.Values.TryGetValue("IconSize", out var size))
                {
                    CurrentIconSize = (ViewMode)(int)size;
                }

                if (settings.Values.TryGetValue("LeftViewMode", out var leftMode))
                {
                    LeftViewMode = (ViewMode)(int)leftMode;
                    CurrentViewMode = LeftViewMode;
                }

                if (settings.Values.TryGetValue("RightViewMode", out var rightMode))
                {
                    RightViewMode = (ViewMode)(int)rightMode;
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
