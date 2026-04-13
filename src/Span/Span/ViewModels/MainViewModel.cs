using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Specialized;
using System.Text.Json;

namespace Span.ViewModels
{
    /// <summary>
    /// 메인 뷰모델. 앱 전체 상태를 관리: 탭, 사이드바(드라이브/즐겨찾기/최근폴더/원격연결),
    /// 듀얼 패인(Split View), 뷰 모드 전환, Undo/Redo 히스토리, 상태바, 토스트 알림.
    /// Tab/ViewMode/FileOperations/SplitPreview 로직은 partial 클래스로 분리.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appTitle = "SPAN Finder";

        public ObservableCollection<TabItem> Tabs { get; } = new();
        public ObservableCollection<DriveItem> Drives { get; } = new();
        public ObservableCollection<DriveItem> NetworkDrives { get; } = new();
        public ObservableCollection<DriveItem> CloudDrives { get; } = new();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new();
        public ObservableCollection<FavoriteItem> RecentFolders { get; } = new();
        public ObservableCollection<Models.ConnectionInfo> SavedConnections { get; } = new();

        /// <summary>
        /// 사이드바 "드라이브" 섹션에 표시할 통합 컬렉션 (로컬 + 네트워크 + 원격 연결)
        /// </summary>
        public ObservableCollection<DriveItem> AllDrives { get; } = new();

        /// <summary>
        /// 네트워크 매핑 드라이브 + 원격 연결 통합 (사이드바 네트워크 그룹용)
        /// </summary>
        public ObservableCollection<DriveItem> NetworkAndRemoteDrives { get; } = new();

        // ── File Shelf ──────────────────────────────────────────
        public ObservableCollection<ShelfItem> ShelfItems { get; } = new();

        /// <summary>사용자가 단축키(Ctrl+B)로 수동 열었는지 여부</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsShelfPanelVisible))]
        private bool _isShelfVisible;

        /// <summary>드래그 중 자동 표시 (드래그 끝나면 아이템 없으면 자동 숨김)</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsShelfPanelVisible))]
        private bool _isShelfDragHover;

        /// <summary>패널 보이기: (수동 토글 OR 드래그 호버 OR 아이템 있음) AND Settings/Log 모드가 아님.
        /// 설정/작업 로그 화면에서는 Shelf 가 콘텐츠를 가리지 않도록 자동 숨김.
        /// 상태(_isShelfVisible/ShelfItems)는 그대로 유지되어 모드 복귀 시 다시 나타남.</summary>
        public Microsoft.UI.Xaml.Visibility IsShelfPanelVisible
            => (_isShelfVisible || _isShelfDragHover || ShelfItems.Count > 0)
               && _currentViewMode != ViewMode.Settings
               && _currentViewMode != ViewMode.ActionLog
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        public void NotifyShelfVisibilityChanged()
            => OnPropertyChanged(nameof(IsShelfPanelVisible));

        // 사이드바 섹션 접기/펴기 상태
        [ObservableProperty]
        private bool _isLocalDrivesExpanded = true;

        [ObservableProperty]
        private bool _isCloudDrivesExpanded = true;

        [ObservableProperty]
        private bool _isNetworkDrivesExpanded = true;

        partial void OnIsLocalDrivesExpandedChanged(bool value)
        {
            if (_isCleaningUp) return;
            try { App.Current.Services.GetRequiredService<SettingsService>().Set("SidebarLocalExpanded", value); } catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] SidebarLocalExpanded save failed: {ex.Message}"); }
        }

        partial void OnIsCloudDrivesExpandedChanged(bool value)
        {
            if (_isCleaningUp) return;
            try { App.Current.Services.GetRequiredService<SettingsService>().Set("SidebarCloudExpanded", value); } catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] SidebarCloudExpanded save failed: {ex.Message}"); }
        }

        partial void OnIsNetworkDrivesExpandedChanged(bool value)
        {
            if (_isCleaningUp) return;
            try { App.Current.Services.GetRequiredService<SettingsService>().Set("SidebarNetworkExpanded", value); } catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] SidebarNetworkExpanded save failed: {ex.Message}"); }
        }

        public bool HasCloudDrives => CloudDrives.Count > 0;
        public bool HasNetworkDrives => NetworkAndRemoteDrives.Count > 0;

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
        /// ActionLog/Settings 탭 변환 시 Explorer를 직접 설정.
        /// backing field 교체 + PropertyChanged 구독 관리.
        /// </summary>
        public void SetLeftExplorer(ExplorerViewModel explorer)
        {
            var old = _leftExplorer;
            if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
            _leftExplorer = explorer;
            if (_leftExplorer != null) _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;
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
        private bool _isLeftPreviewEnabled = true;

        [ObservableProperty]
        private bool _isRightPreviewEnabled = true;

        private readonly FileSystemService _fileService;
        private readonly FavoritesService _favoritesService;
        private readonly ActionLogService _actionLogService;
        private readonly FileOperationHistory _operationHistory;
        private readonly FileOperationProgressViewModel _progressViewModel;
        private readonly FileOperationManager _fileOperationManager;
        private readonly System.Threading.CancellationTokenSource _shutdownCts = new();
        private bool _isCleaningUp = false;
        private Services.CloudStorageProviderService? _cloudStorageProvider;
        private const int MaxRecentFolders = 20;

        // Stored delegates for event unsubscription (memory leak prevention)
        private Action<string, object?>? _settingChangedHandler;
        private System.ComponentModel.PropertyChangedEventHandler? _rightExplorerPropertyChangedHandler;
        private NotifyCollectionChangedEventHandler? _savedConnectionsCollectionChangedHandler;

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

        [ObservableProperty]
        private bool _isToastError = false;

        private System.Threading.Timer? _toastTimer;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsShelfPanelVisible))]
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

        private readonly LocalizationService _loc;

        public MainViewModel(FileSystemService fileService, FavoritesService favoritesService, ActionLogService actionLogService)
        {
            _fileService = fileService;
            _favoritesService = favoritesService;
            _actionLogService = actionLogService;
            _loc = App.Current.Services.GetRequiredService<LocalizationService>();
            _operationHistory = new FileOperationHistory();
            _progressViewModel = new FileOperationProgressViewModel();
            _fileOperationManager = App.Current.Services.GetRequiredService<FileOperationManager>();
            _progressViewModel.OperationManager = _fileOperationManager;

            // Apply UndoHistorySize setting
            var settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
            _operationHistory.MaxHistorySize = settings.UndoHistorySize;
            _settingChangedHandler = (key, value) =>
            {
                if (key == "UndoHistorySize" && value is int size)
                    _operationHistory.MaxHistorySize = size;
            };
            settings.SettingChanged += _settingChangedHandler;

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

            // 재귀 검색 상태 변경 → 상태바 업데이트
            if (e.PropertyName == nameof(ExplorerViewModel.SearchStatusText) ||
                e.PropertyName == nameof(ExplorerViewModel.IsRecursiveSearching))
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

            // Settings/Home/ActionLog 모드에서는 상태바 표시 불필요
            if (CurrentViewMode == ViewMode.Settings || CurrentViewMode == ViewMode.Home || CurrentViewMode == ViewMode.ActionLog)
            {
                StatusItemCountText = "";
                StatusSelectionText = "";
                StatusDiskSpaceText = "";
                var modeText = Helpers.ViewModeExtensions.GetDisplayName(CurrentViewMode);
                StatusViewModeText = modeText;
                return;
            }

            // RecycleBin 모드: RecycleBinModeView에서 전달받은 상태 표시
            if (CurrentViewMode == ViewMode.RecycleBin)
            {
                string itemCountBase = string.Format(_loc.Get("StatusItemCount") ?? "{0} items", RecycleBinViewItemCount);
                StatusItemCountText = itemCountBase;
                if (RecycleBinViewSelectedCount > 0)
                {
                    StatusSelectionText = string.Format(_loc.Get("StatusSelected") ?? "{0} selected", RecycleBinViewSelectedCount);
                }
                else
                {
                    StatusSelectionText = "";
                }
                StatusDiskSpaceText = "";
                StatusViewModeText = Helpers.ViewModeExtensions.GetDisplayName(ViewMode.RecycleBin);
                return;
            }

            var explorer = ActiveExplorer;
            // 포커스된(활성) 컬럼 기준으로 상태바 표시 (IsActive 플래그 사용)
            var folder = explorer?.Columns.FirstOrDefault(c => c.IsActive) ?? explorer?.CurrentFolder;
            int itemCount = folder?.Children?.Count ?? 0;

            // Selection: SelectedItems.Count 기준 (SelectedChild는 탐색 전용이라 상태바에 부적합)
            int selCount = folder?.SelectedItems.Count ?? 0;

            // 재귀 검색 중이면 검색 상태 표시
            if (!string.IsNullOrEmpty(explorer?.SearchStatusText))
            {
                StatusItemCountText = explorer.SearchStatusText;
            }
            else
            {
                StatusItemCountText = string.Format(_loc.Get("StatusItemCount") ?? "{0} items", itemCount);
            }
            if (selCount > 0)
            {
                var selText = string.Format(_loc.Get("StatusSelected") ?? "{0} selected", selCount);
                // Calculate total size of selected items
                long totalSize = 0;
                if (folder?.SelectedItems != null)
                {
                    foreach (var item in folder.SelectedItems)
                    {
                        totalSize += item.SizeValue;
                    }
                }
                if (totalSize > 0)
                {
                    selText += $" ({FormatFileSizeCompact(totalSize)})";
                }
                StatusSelectionText = selText;
            }
            else
            {
                StatusSelectionText = "";
            }

            var mode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            StatusViewModeText = Helpers.ViewModeExtensions.GetDisplayName(mode);

            // Disk space info
            StatusDiskSpaceText = GetDiskSpaceText(explorer?.CurrentPath);
        }

        private static string FormatFileSizeCompact(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            > 0 => $"{bytes} B",
            _ => ""
        };

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

                return string.Format(LocalizationService.L("StatusBar_DiskSpace"), FormatDiskSize(driveInfo.AvailableFreeSpace), FormatDiskSize(driveInfo.TotalSize));
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
            _ = LoadDrivesAsync();
            LoadFavorites();
            LoadRecentFolders();
            _ = LoadSavedConnectionsAsync();
            _ = RefreshRecycleBinInfoAsync();

            // Load ViewMode preference (includes split state)
            LoadViewModePreference();

            // Restore sidebar section expand state
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                _isLocalDrivesExpanded = settings.Get("SidebarLocalExpanded", true);
                _isCloudDrivesExpanded = settings.Get("SidebarCloudExpanded", true);
                _isNetworkDrivesExpanded = settings.Get("SidebarNetworkExpanded", true);
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] Sidebar expand state load failed: {ex.Message}"); }

            // LeftExplorer PropertyChanged는 setter에서 자동 구독됨
            // RightExplorer는 탭과 무관하므로 별도 구독
            _rightExplorerPropertyChangedHandler = (s, e) =>
            {
                if (_isCleaningUp) return;
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) && !string.IsNullOrEmpty(RightExplorer?.CurrentPath))
                {
                    AddRecentFolder(RightExplorer.CurrentPath);
                }
            };
            RightExplorer.PropertyChanged += _rightExplorerPropertyChangedHandler;
        }

        private void EnsureDefaultTab()
        {
            if (Tabs.Count == 0)
            {
                var tab = new TabItem { Header = LocalizationService.L("Home_TabName"), ViewMode = ViewMode.Home, IsActive = true };
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
            _ = LoadDrivesAsync();
            Helpers.DebugLogger.Log("[MainViewModel] RefreshDrives triggered by device change");
        }

        private async Task LoadDrivesAsync()
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

                // Step 4: Network Shortcuts (네트워크 위치 — Windows 탐색기의 "네트워크 위치" 항목)
                var networkShortcuts = await _fileService.GetNetworkShortcutsAsync();

                // Step 5: Skip UI update if drives haven't changed (avoid flicker)
                var newLocalDrives = drives.Where(d => !d.IsNetworkDrive).ToList();
                var newNetworkDrives = drives.Where(d => d.IsNetworkDrive).ToList();
                // 네트워크 위치(바로가기)도 네트워크 그룹에 추가
                // FTP URL 바로가기 중 저장된 연결이 없으면 NeedsAuth 플래그 설정
                foreach (var ns in networkShortcuts)
                {
                    if (ns.Path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                        ns.Path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var uri = new Uri(ns.Path);
                            var hasConn = SavedConnections.Any(c =>
                                c.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) &&
                                (c.Protocol == Models.RemoteProtocol.FTP || c.Protocol == Models.RemoteProtocol.FTPS));
                            ns.NeedsAuth = !hasConn;
                        }
                        catch { ns.NeedsAuth = true; }
                    }
                }
                newNetworkDrives.AddRange(networkShortcuts);

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

                // Step 6: Detect cloud storage providers (iCloud, OneDrive, Dropbox, etc.)
                _cloudStorageProvider ??= new CloudStorageProviderService();
                var newCloudDrives = await Task.Run(() => _cloudStorageProvider.GetCloudStorageDrives());

                // Filter out cloud drives that overlap with physical drives
                var physicalPaths = new HashSet<string>(
                    drives.Select(d => d.Path.TrimEnd('\\')),
                    StringComparer.OrdinalIgnoreCase);
                newCloudDrives.RemoveAll(c => physicalPaths.Contains(c.Path.TrimEnd('\\')));

                if (!AreDriveListsEqual(CloudDrives, newCloudDrives))
                {
                    CloudDrives.Clear();
                    foreach (var cd in newCloudDrives)
                        CloudDrives.Add(cd);
                }

                // Step 6b: 클라우드 경로를 CloudSyncService에 등록 (아이콘 오버레이용)
                var cloudSync = App.Current.Services.GetService(typeof(CloudSyncService)) as CloudSyncService;
                if (cloudSync != null)
                {
                    foreach (var cd in newCloudDrives)
                        cloudSync.RegisterCloudRoot(cd.Path);
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {Drives.Count} local + {NetworkDrives.Count} network + {CloudDrives.Count} cloud drives (changed={drivesChanged})");

                // Step 7: Rebuild unified AllDrives collection
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
        /// 로컬 + 클라우드 + 네트워크 + 원격 연결을 AllDrives에 통합
        /// 순서: 로컬 드라이브 → 클라우드 스토리지 → 네트워크 매핑 드라이브 → SMB → FTP/FTPS → SFTP (이름순)
        /// </summary>
        private void RebuildAllDrives()
        {
            // AllDrives: 하위 호환용 (전체 통합)
            AllDrives.Clear();
            foreach (var d in Drives)
                AllDrives.Add(d);
            foreach (var d in CloudDrives)
                AllDrives.Add(d);
            foreach (var d in NetworkDrives)
                AllDrives.Add(d);

            var sortedConnections = SavedConnections
                .OrderBy(c => c.Protocol switch
                {
                    Models.RemoteProtocol.SMB => 0,
                    Models.RemoteProtocol.FTP => 1,
                    Models.RemoteProtocol.FTPS => 2,
                    Models.RemoteProtocol.SFTP => 3,
                    _ => 9
                })
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);
            foreach (var conn in sortedConnections)
                AllDrives.Add(DriveItem.FromConnection(conn));

            // NetworkAndRemoteDrives: 네트워크 매핑 + 원격 연결 통합
            // 네트워크 바로가기와 중복되는 SavedConnections는 제외 (호스트 매칭)
            var shortcutHosts = new HashSet<string>(
                NetworkDrives.Where(d => d.IsNetworkShortcut && d.Path.Contains("://"))
                    .Select(d => { try { return new Uri(d.Path).Host; } catch { return ""; } })
                    .Where(h => !string.IsNullOrEmpty(h)),
                StringComparer.OrdinalIgnoreCase);

            NetworkAndRemoteDrives.Clear();
            foreach (var d in NetworkDrives)
                NetworkAndRemoteDrives.Add(d);
            foreach (var conn in sortedConnections)
            {
                // 네트워크 바로가기와 호스트가 겹치면 스킵 (중복 방지)
                if (shortcutHosts.Contains(conn.Host)) continue;
                NetworkAndRemoteDrives.Add(DriveItem.FromConnection(conn));
            }

            // 가시성 알림
            OnPropertyChanged(nameof(HasCloudDrives));
            OnPropertyChanged(nameof(HasNetworkDrives));
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

                // 서버 연결 데이터 즉시 저장 (미완료 비동기 저장이 있을 수 있음)
                try
                {
                    var connectionService = App.Current.Services.GetRequiredService<Services.ConnectionManagerService>();
                    connectionService.FlushSync();
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] Cleanup: FlushSync failed: {ex.Message}"); }

                // Unsubscribe stored event handlers to prevent memory leaks
                if (_settingChangedHandler != null)
                {
                    try
                    {
                        var settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
                        settings.SettingChanged -= _settingChangedHandler;
                    }
                    catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] Cleanup: SettingChanged unsubscribe failed: {ex.Message}"); }
                    _settingChangedHandler = null;
                }

                if (RightExplorer != null && _rightExplorerPropertyChangedHandler != null)
                {
                    RightExplorer.PropertyChanged -= _rightExplorerPropertyChangedHandler;
                    _rightExplorerPropertyChangedHandler = null;
                }

                if (_savedConnectionsCollectionChangedHandler != null)
                {
                    try
                    {
                        var connectionService = App.Current.Services.GetRequiredService<Services.ConnectionManagerService>();
                        connectionService.SavedConnections.CollectionChanged -= _savedConnectionsCollectionChangedHandler;
                    }
                    catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] Cleanup: ConnectionsChanged unsubscribe failed: {ex.Message}"); }
                    _savedConnectionsCollectionChangedHandler = null;
                }

                // MUST set before clearing collections to prevent
                // ObservableCollection change notifications reaching disposed UI
                _isCleaningUp = true;

                // Cancel any ongoing background operations
                try { _shutdownCts?.Cancel(); } catch (ObjectDisposedException) { }
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
                NetworkAndRemoteDrives.Clear();
                Drives.Clear();
                NetworkDrives.Clear();
                CloudDrives.Clear();
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
                            IconGlyph = Services.IconService.Current?.DriveGlyph ?? "\uEDFA"
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
                    Name = d.Name,
                    Path = d.Path,
                    Label = d.Label,
                    TotalSize = d.TotalSize,
                    AvailableFreeSpace = d.AvailableFreeSpace,
                    DriveFormat = d.DriveFormat,
                    DriveType = d.DriveType,
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
                LocalizeFavoriteNames();
                Helpers.DebugLogger.Log($"[MainViewModel] Loaded {items.Count} favorites");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading favorites: {ex.Message}");
            }
        }

        /// <summary>
        /// 알려진 폴더(Desktop, Downloads 등)의 즐겨찾기 이름을 앱 언어로 변환.
        /// </summary>
        internal void LocalizeFavoriteNames()
        {
            var loc = App.Current.Services.GetService<LocalizationService>();
            if (loc == null) return;

            var knownFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Environment.GetFolderPath(Environment.SpecialFolder.Desktop)] = loc.Get("KnownFolder_Desktop"),
                [System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")] = loc.Get("KnownFolder_Downloads"),
                [Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)] = loc.Get("KnownFolder_Documents"),
                [System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures")] = loc.Get("KnownFolder_Pictures"),
                [Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)] = loc.Get("KnownFolder_Music"),
                [Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)] = loc.Get("KnownFolder_Videos"),
            };

            bool changed = false;
            foreach (var fav in Favorites)
            {
                if (knownFolders.TryGetValue(fav.Path, out var localizedName) && fav.Name != localizedName)
                {
                    fav.Name = localizedName;
                    changed = true;
                }
            }

            // FavoriteItem에 INotifyPropertyChanged가 없으므로 컬렉션 교체로 UI 갱신
            if (changed)
            {
                var snapshot = Favorites.ToList();
                Favorites.Clear();
                foreach (var item in snapshot)
                    Favorites.Add(item);
            }
        }

        public void AddToFavorites(string path)
        {
            if (Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return;

            var updated = _favoritesService.AddFavorite(path, Favorites.ToList());

            Favorites.Clear();
            foreach (var item in updated)
            {
                Favorites.Add(item);
            }
            Helpers.DebugLogger.Log($"[MainViewModel] Added to favorites (Quick Access): {path}");
        }

        public void RemoveFromFavorites(string path)
        {
            var updated = _favoritesService.RemoveFavorite(path, Favorites.ToList());

            Favorites.Clear();
            foreach (var item in updated)
            {
                Favorites.Add(item);
            }
            Helpers.DebugLogger.Log($"[MainViewModel] Removed from favorites (Quick Access): {path}");
        }

        public bool IsFavorite(string path)
        {
            return Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 사이드바 즐겨찾기 클릭 시 해당 경로로 탐색.
        /// Home/ActionLog 모드인 경우 이전 뷰모드를 복원하여 사용자 뷰 컨텍스트 유지.
        /// OpenDrive()와 동일한 뷰모드 보존 패턴 사용.
        /// </summary>
        public void NavigateToFavorite(FavoriteItem favorite)
        {
            if (!System.IO.Directory.Exists(favorite.Path))
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Favorite path not found: {favorite.Path}");
                return;
            }

            // Home/ActionLog/RecycleBin에서 벗어나며 이전 뷰모드 복원 (OpenDrive와 동일한 패턴)
            var activeViewMode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            if (activeViewMode == ViewMode.Home || activeViewMode == ViewMode.ActionLog
                || activeViewMode == ViewMode.RecycleBin)
            {
                SwitchViewMode(ResolveViewModeFromHome());
            }

            var folder = new FolderItem { Name = favorite.Name, Path = favorite.Path };
            _ = ActiveExplorer?.NavigateTo(folder);
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

                // Update Jump List
                _ = UpdateJumpListAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving recent folders: {ex.Message}");
            }
        }

        private async Task UpdateJumpListAsync()
        {
            try
            {
                var jumpListService = App.Current.Services.GetService<Services.JumpListService>();
                if (jumpListService == null) return;
                var folders = RecentFolders.Select(r => (r.Name, r.Path));
                await jumpListService.UpdateAsync(folders);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] JumpList update error: {ex.Message}");
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

        private async Task LoadSavedConnectionsAsync()
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
                _savedConnectionsCollectionChangedHandler = (s, e) =>
                {
                    SavedConnections.Clear();
                    foreach (var c in connectionService.SavedConnections)
                        SavedConnections.Add(c);
                    RebuildAllDrives();
                };
                connectionService.SavedConnections.CollectionChanged += _savedConnectionsCollectionChangedHandler;

                Helpers.DebugLogger.Log($"[MainViewModel] {SavedConnections.Count}개의 저장된 연결 로드");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] 저장된 연결 로드 오류: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Settings 탭을 열거나, 이미 열려있으면 해당 탭으로 전환.
        /// Settings 탭은 Explorer가 null이며, 최대 1개만 허용.
        /// </summary>
        public void OpenOrSwitchToSettingsTab()
        {
            // 기존 Settings 탭 검색
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].ViewMode == ViewMode.Settings)
                {
                    if (i != ActiveTabIndex)
                        SwitchToTab(i);
                    return;
                }
            }

            // 새 Settings 탭 생성 (Explorer 없음)
            var tab = new TabItem
            {
                Header = App.Current.Services.GetService<LocalizationService>()?.Get("Settings") ?? "Settings",
                Path = "",
                ViewMode = ViewMode.Settings,
                IconSize = ViewMode.IconMedium,
                IsActive = false,
                Explorer = null
            };
            Tabs.Add(tab);
            SwitchToTab(Tabs.Count - 1);
            Helpers.DebugLogger.Log($"[MainViewModel] Settings tab opened (total: {Tabs.Count})");
        }

        /// <summary>
        /// 작업 로그 탭을 열거나, 이미 열려있으면 해당 탭으로 전환.
        /// ActionLog 탭은 Explorer가 null이며, 최대 1개만 허용.
        /// </summary>
        public void OpenOrSwitchToActionLogTab()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].ViewMode == ViewMode.ActionLog)
                {
                    if (i != ActiveTabIndex)
                        SwitchToTab(i);
                    return;
                }
            }

            var tab = new TabItem
            {
                Header = App.Current.Services.GetService<LocalizationService>()?.Get("Log_Title") ?? "Action Log",
                Path = "",
                ViewMode = ViewMode.ActionLog,
                IconSize = ViewMode.IconMedium,
                IsActive = false,
                Explorer = null
            };
            Tabs.Add(tab);
            SwitchToTab(Tabs.Count - 1);
            Helpers.DebugLogger.Log($"[MainViewModel] ActionLog tab opened (total: {Tabs.Count})");
        }

        #region Recycle Bin

        // RecycleBin view status (set by RecycleBinModeView via StatusChanged event)
        internal int RecycleBinViewItemCount { get; set; }
        internal int RecycleBinViewSelectedCount { get; set; }
        internal long RecycleBinViewTotalSize { get; set; }
        internal long RecycleBinViewSelectedSize { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RecycleBinIsEmpty))]
        [NotifyPropertyChangedFor(nameof(RecycleBinIconGlyph))]
        private long _recycleBinItemCount;

        public bool RecycleBinIsEmpty => RecycleBinItemCount == 0;

        /// <summary>
        /// 사이드바 휴지통 아이콘. 비어있으면 아웃라인, 항목 있으면 채워진 아이콘.
        /// </summary>
        public string RecycleBinIconGlyph => RecycleBinIsEmpty
            ? (Services.IconService.Current?.RecycleBinEmptyGlyph ?? "\uEB2A")
            : (Services.IconService.Current?.RecycleBinFullGlyph ?? "\uEB29");

        /// <summary>
        /// 현재 활성 탭이 휴지통 탭인지 여부.
        /// </summary>
        public bool IsRecycleBinTab => ActiveTab?.ViewMode == ViewMode.RecycleBin;

        /// <summary>
        /// SHQueryRecycleBin으로 휴지통 상태를 빠르게 갱신 (사이드바 아이콘/배지용).
        /// 호출 시점: 앱 시작, 삭제 작업 완료 후, 휴지통 비우기 후, 탭 전환 시.
        /// </summary>
        public async Task RefreshRecycleBinInfoAsync()
        {
            try
            {
                var service = App.Current.Services.GetRequiredService<RecycleBinService>();
                var info = await service.GetInfoAsync();
                RecycleBinItemCount = info.ItemCount;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] RefreshRecycleBinInfo failed: {ex.Message}");
            }
        }

        // OpenOrSwitchToRecycleBinTab / ConvertRecycleBinTabToNormal 제거됨
        // RecycleBin은 Home과 동일하게 SwitchViewMode(ViewMode.RecycleBin)으로 전환

        #endregion

    }
}
