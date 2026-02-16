using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        public ObservableCollection<FavoriteItem> Favorites { get; } = new();
        public ObservableCollection<FavoriteItem> RecentFolders { get; } = new();

        // Engine
        private ExplorerViewModel _explorer;
        public ExplorerViewModel Explorer
        {
            get => _explorer;
            set => SetProperty(ref _explorer, value);
        }

        private readonly FileSystemService _fileService;
        private readonly FavoritesService _favoritesService;
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
        private ViewMode _currentViewMode = ViewMode.MillerColumns;

        [ObservableProperty]
        private ViewMode _currentIconSize = ViewMode.IconMedium; // Icon 모드 기본 크기

        public FileOperationProgressViewModel ProgressViewModel => _progressViewModel;

        public MainViewModel(FileSystemService fileService, FavoritesService favoritesService)
        {
            _fileService = fileService;
            _favoritesService = favoritesService;
            _operationHistory = new FileOperationHistory();
            _progressViewModel = new FileOperationProgressViewModel();

            _operationHistory.HistoryChanged += OnHistoryChanged;

            Initialize();
        }

        private void Initialize()
        {
            // Dummy tabs
            Tabs.Add(new TabItem { Header = "Project Span", Icon = "\uEA34" }); // ri-apps-2-fill

            // Initialize Engine with a conceptual Root or just empty
            // To make sure UI binds correctly, we start with a dummy or a specific path if possible.
            // Let's start with "My Computer" concept or just C:\
            var root = new FolderItem { Name = "PC", Path = "PC" }; /* Virtual Root */
            Explorer = new ExplorerViewModel(root, _fileService);

            // Populate Sidebar
            LoadDrives();
            LoadFavorites();
            LoadRecentFolders();

            // Load ViewMode preference
            LoadViewModePreference();

            // Track navigation for recent folders
            Explorer.PropertyChanged += (s, e) =>
            {
                if (_isCleaningUp) return;
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(Explorer.CurrentPath))
                {
                    AddRecentFolder(Explorer.CurrentPath);
                }
            };
        }

        private async void LoadDrives()
        {
            try
            {
                // Step 1: Load from cache immediately (fast)
                var cachedDrives = LoadDrivesFromCache();
                if (cachedDrives.Count > 0)
                {
                    Drives.Clear();
                    foreach (var drive in cachedDrives)
                    {
                        Drives.Add(drive);
                    }
                    Helpers.DebugLogger.Log($"[MainViewModel] Loaded {cachedDrives.Count} drives from cache");
                }

                // Step 2: Refresh from file system in background (accurate)
                var drives = await _fileService.GetDrivesAsync();

                // Step 3: Check if we're shutting down before updating UI
                if (_shutdownCts.Token.IsCancellationRequested)
                {
                    Helpers.DebugLogger.Log("[MainViewModel] LoadDrives cancelled - app is shutting down");
                    return;
                }

                // Step 4: Update UI and cache
                Drives.Clear();
                foreach (var drive in drives)
                {
                    Drives.Add(drive);
                }

                // Step 5: Save updated list to cache
                SaveDrivesCache(drives);
                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {drives.Count} drives from file system");
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

                // MUST set before clearing collections to prevent
                // ObservableCollection change notifications reaching disposed UI
                _isCleaningUp = true;

                // Cancel any ongoing background operations
                _shutdownCts?.Cancel();

                // Clear collections (safe now - _isCleaningUp suppresses side effects)
                Drives.Clear();
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
                                DriveType = driveData["DriveType"] as string ?? ""
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
                        ["DriveType"] = drive.DriveType
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
            // When a drive is clicked, navigate Explorer to it.
            var driveRoot = new FolderItem
            {
                Name = drive.Name,
                Path = drive.Path
            };

            // Re-initialize Explorer or Navigate?
            // Since we want to clear previous columns and start fresh from this drive:
            Explorer.NavigateTo(driveRoot);
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
            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Columns: {string.Join(" > ", Explorer.Columns.Select(c => c.Name))}");

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

        private async Task RefreshCurrentFolderAsync(int? columnIndex = null)
        {
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] START - columnIndex: {columnIndex}");

            if (Explorer?.Columns == null || Explorer.Columns.Count == 0)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] No columns to refresh - ABORT");
                return;
            }

            // Determine which column to refresh
            // If columnIndex is provided, use it; otherwise refresh the last column
            int targetIndex = columnIndex ?? Explorer.Columns.Count - 1;
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Target index: {targetIndex} (total columns: {Explorer.Columns.Count})");

            // Validate index
            if (targetIndex < 0 || targetIndex >= Explorer.Columns.Count)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Invalid index - ABORT");
                return;
            }

            var targetColumn = Explorer.Columns[targetIndex];
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

        private void ShowToast(string message)
        {
            // TODO: Implement toast notification
            StatusBarText = message;
        }

        private void ShowError(string message)
        {
            // TODO: Implement error dialog
            StatusBarText = $"Error: {message}";
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
            if (CurrentViewMode == ViewMode.Home)
            {
                SwitchViewMode(ViewMode.MillerColumns);
            }

            var folder = new FolderItem { Name = favorite.Name, Path = favorite.Path };
            Explorer.NavigateTo(folder);
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
        /// 뷰 모드 전환
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            if (CurrentViewMode == mode) return;

            // Icon 모드 전환 시 크기 업데이트
            if (Helpers.ViewModeExtensions.IsIconMode(mode))
            {
                CurrentIconSize = mode;
                // UI에서는 Icon 통합 표시를 위해 IconMedium으로 설정하지만,
                // 실제 크기는 CurrentIconSize로 구분
                CurrentViewMode = mode; // Icon 계열은 각각 독립적인 ViewMode
            }
            else
            {
                CurrentViewMode = mode;
            }

            // CRITICAL: Enable auto-navigation only in Miller Columns mode
            // In Details/Icon/Home modes, disable auto-navigation (use double-click instead)
            if (mode != ViewMode.Home)
            {
                Explorer.EnableAutoNavigation = (mode == ViewMode.MillerColumns);
            }
            Helpers.DebugLogger.Log($"[MainViewModel] Auto-navigation: {Explorer.EnableAutoNavigation} (mode: {mode})");

            SaveViewModePreference();
            Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: {Helpers.ViewModeExtensions.GetDisplayName(mode)}");
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
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode saved: {CurrentViewMode}, IconSize: {CurrentIconSize}");
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
                }

                if (settings.Values.TryGetValue("IconSize", out var size))
                {
                    CurrentIconSize = (ViewMode)(int)size;
                }

                // Set auto-navigation based on loaded view mode
                Explorer.EnableAutoNavigation = (CurrentViewMode == ViewMode.MillerColumns);
                Helpers.DebugLogger.Log($"[MainViewModel] Auto-navigation: {Explorer.EnableAutoNavigation}");

                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode loaded: {Helpers.ViewModeExtensions.GetDisplayName(CurrentViewMode)}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadViewModePreference error: {ex.Message}");
                CurrentViewMode = ViewMode.MillerColumns; // Fallback
                Explorer.EnableAutoNavigation = true; // Fallback to Miller mode
            }
        }
    }
}
