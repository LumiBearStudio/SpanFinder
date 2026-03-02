using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Span.Models;
using System.Threading;
using System.Threading.Tasks;
using Span.Services;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Span.ViewModels
{
    /// <summary>
    /// 폴더 뷰모델. 지연 로딩(EnsureChildrenLoadedAsync), 정렬(Name/Date/Type/Size),
    /// FolderContentCache 캐시 연동, 원격 폴더 로딩(FTP/SFTP/SMB), 멀티 선택,
    /// on-demand 클라우드/Git 상태 주입, 폴더 크기 비동기 계산을 지원.
    /// </summary>
    public partial class FolderViewModel : FileSystemViewModel
    {
        private readonly FileSystemService _fileService;
        private readonly FolderItem _folderModel;
        private bool _isLoaded = false;
        private string? _calculatedSize;
        private Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;

        /// <summary>
        /// 이 폴더가 클라우드 경로인지 캐시 (on-demand cloud state 주입용).
        /// </summary>
        private bool _isCloudFolder;
        private CloudSyncService? _cloudSvc;

        /// <summary>
        /// Git 레포 여부 캐시 (on-demand git state 주입용).
        /// </summary>
        private bool _isGitFolder;
        private GitStatusService? _gitSvc;

        [ObservableProperty]
        private ObservableCollection<FileSystemViewModel> _children = new();

        /// <summary>
        /// Children을 원자적으로 교체. Clear+Add 대신 새 컬렉션을 할당하여
        /// 28K CollectionChanged 이벤트를 단일 Reset으로 줄인다.
        /// </summary>
        public void ReplaceChildren(System.Collections.Generic.IList<FileSystemViewModel> newItems)
        {
            Children = new ObservableCollection<FileSystemViewModel>(newItems);
        }

        /// <summary>
        /// 필터링 전 전체 아이템 목록. 필터 활성 시 Children은 이 리스트의 부분집합.
        /// </summary>
        private List<FileSystemViewModel>? _allChildren;
        private string _currentFilterText = string.Empty;

        /// <summary>
        /// 필터링/정렬 중 Children 교체로 인한 PropertyChanged 연쇄를 차단하기 위한 가드.
        /// true일 때 ExplorerViewModel.FolderVm_PropertyChanged가 Children 변경을 무시.
        /// </summary>
        private bool _isBulkUpdating;
        internal bool IsBulkUpdating => _isBulkUpdating;

        /// <summary>
        /// 현재 적용 중인 필터 텍스트 (ExplorerViewModel에서 전파 확인용).
        /// </summary>
        internal string CurrentFilterText => _currentFilterText;

        /// <summary>
        /// 전체 아이템 수 (필터 적용 전). 필터 비활성 시 Children.Count와 동일.
        /// </summary>
        public int TotalChildCount => _allChildren?.Count ?? Children.Count;

        [ObservableProperty]
        private FileSystemViewModel? _selectedChild;

        /// <summary>
        /// Multi-selection: tracks all selected items in this column.
        /// Updated via SyncSelectedItems() from ListView.SelectionChanged.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<FileSystemViewModel> _selectedItems = new();

        /// <summary>
        /// True when more than one item is selected.
        /// Used to suppress auto-navigation in Miller Columns.
        /// </summary>
        public bool HasMultiSelection => SelectedItems.Count > 1;

        [ObservableProperty]
        private bool _isLoading = false;

        /// <summary>
        /// True when loading completed but the folder has no children.
        /// </summary>
        [ObservableProperty]
        private bool _isEmpty = false;

        /// <summary>
        /// Error message shown when folder loading fails (e.g., access denied, path too long).
        /// </summary>
        [ObservableProperty]
        private string? _errorMessage;

        /// <summary>
        /// Segoe Fluent icon glyph for the error state.
        /// </summary>
        [ObservableProperty]
        private string? _errorIcon;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// 폴더 로딩 실패 시 에러 메시지를 전파하는 이벤트.
        /// ExplorerViewModel에서 구독하여 토스트 알림으로 버블링.
        /// </summary>
        public event Action<string>? LoadError;

        partial void OnErrorMessageChanged(string? value)
        {
            OnPropertyChanged(nameof(HasError));
            if (!string.IsNullOrEmpty(value))
                LoadError?.Invoke(value);
        }

        [ObservableProperty]
        private bool _isActive = false; // Indicates if this column has focus

        /// <summary>
        /// 정렬 중 플래그 - true일 때 PropertyChanged 이벤트 무시
        /// </summary>
        public bool IsSorting { get; set; } = false;

        /// <summary>
        /// 이미 로드 완료된 폴더인지 확인 (디바운스 건너뛰기용).
        /// </summary>
        public bool IsAlreadyLoaded => _isLoaded;

        /// <summary>
        /// 검색 결과용 가상 폴더로 표시하여 EnsureChildrenLoadedAsync()에서
        /// 디스크 I/O를 시도하지 않도록 함.
        /// </summary>
        internal void MarkAsManuallyPopulated()
        {
            _isLoaded = true;
            IsLoading = false;
        }

        public override string IconGlyph => Services.IconService.Current.FolderIcon;
        public override Microsoft.UI.Xaml.Media.Brush IconBrush => Services.IconService.Current.FolderBrush;

        /// <summary>
        /// 폴더 크기: 백그라운드 계산 완료 시 표시, 미완료 시 빈칸.
        /// </summary>
        public override string Size => _calculatedSize ?? string.Empty;

        public override long SizeValue
        {
            get
            {
                var svc = App.Current.Services.GetService(typeof(FolderSizeService)) as FolderSizeService;
                return svc?.TryGetCachedSize(Path) ?? 0;
            }
        }

        /// <summary>
        /// 폴더 크기 계산 요청 (Details 뷰에서 호출).
        /// </summary>
        public void RequestFolderSizeCalculation()
        {
            if (_calculatedSize != null) return; // 이미 계산됨

            var svc = App.Current.Services.GetService(typeof(FolderSizeService)) as FolderSizeService;
            if (svc == null) return;

            var cached = svc.TryGetCachedSize(Path);
            if (cached.HasValue)
            {
                _calculatedSize = cached.Value >= 0 ? FormatFolderSize(cached.Value) : string.Empty;
                OnPropertyChanged(nameof(Size));
                OnPropertyChanged(nameof(SizeValue));
                return;
            }

            // UI 스레드의 DispatcherQueue를 캡처 (콜백은 백그라운드 스레드에서 호출됨)
            _uiDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            svc.SizeCalculated += OnFolderSizeCalculated;
            svc.RequestCalculation(Path);
        }

        private void OnFolderSizeCalculated(string folderPath, long bytes)
        {
            if (!folderPath.Equals(Path, System.StringComparison.OrdinalIgnoreCase)) return;

            var svc = App.Current.Services.GetService(typeof(FolderSizeService)) as FolderSizeService;
            if (svc != null) svc.SizeCalculated -= OnFolderSizeCalculated;

            _calculatedSize = bytes >= 0 ? FormatFolderSize(bytes) : string.Empty;

            // UI 스레드에서 PropertyChanged 발생
            // OnFolderSizeCalculated는 FolderSizeService의 백그라운드 스레드에서 호출되므로
            // 반드시 캡처된 UI DispatcherQueue를 사용해야 함 (GetForCurrentThread()는 항상 null)
            try
            {
                if (_uiDispatcher != null)
                {
                    _uiDispatcher.TryEnqueue(() =>
                    {
                        OnPropertyChanged(nameof(Size));
                        OnPropertyChanged(nameof(SizeValue));
                    });
                }
                else
                {
                    Helpers.DebugLogger.Log("[FolderViewModel] UI dispatcher not captured, size update skipped");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FolderViewModel] Size update dispatch error: {ex.Message}");
            }
        }

        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

        private static string FormatFolderSize(long bytes)
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

        /// <summary>
        /// Item count text for folder badge display.
        /// Shows the number of child items once loaded, empty string if not loaded or zero.
        /// </summary>
        public string ChildCountText
        {
            get
            {
                if (!_isLoaded || Children.Count == 0)
                    return string.Empty;
                // 필터 활성 시 "X/Y" 형식 표시
                if (!string.IsNullOrEmpty(_currentFilterText) && _allChildren != null)
                    return $"{Children.Count}/{_allChildren.Count}";
                return Children.Count.ToString();
            }
        }

        public FolderViewModel(FolderItem model, FileSystemService fileService) : base(model)
        {
            _folderModel = model;
            _fileService = fileService;
            // DO NOT load children here. Lazy loading only.
        }

        private System.Threading.CancellationTokenSource? _cts;

        /// <summary>
        /// Lazy load: only called when this folder becomes a visible column.
        /// </summary>
        public async Task EnsureChildrenLoadedAsync()
        {
            if (_isLoaded) return;

            _isLoaded = true;
            IsLoading = true;

            _cts?.Cancel();
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // Capture settings on UI thread before background work
                bool showHidden = false;
                Services.FolderContentCache? folderCache = null;
                try
                {
                    var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                    if (settings != null) showHidden = settings.ShowHiddenFiles;
                    folderCache = App.Current.Services.GetService(typeof(Services.FolderContentCache)) as Services.FolderContentCache;
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[FolderViewModel] Service resolution failed: {ex.Message}"); }

                var folderPath = _folderModel.Path;

                var cached = folderCache?.TryGet(folderPath, showHidden);
                if (cached != null)
                {
                    LoadFromCache(cached, token);
                }
                else if (FileSystemRouter.IsRemotePath(folderPath))
                {
                    await LoadFromRemoteAsync(folderPath, token);
                }
                else
                {
                    await LoadFromDiskAsync(folderPath, showHidden, folderCache, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[EnsureChildrenLoadedAsync] 예외: {ex.Message}");
                _isLoaded = false;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoading = false;
                    IsEmpty = Children.Count == 0 && !HasError;
                }
                if (_cts?.Token == token)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private void LoadFromCache(Services.FolderContentCache.CachedFolder cached, System.Threading.CancellationToken token)
        {
            var items = new List<FileSystemViewModel>();
            foreach (var d in cached.Folders)
                items.Add(new FolderViewModel(d, _fileService));
            foreach (var f in cached.Files)
                items.Add(new FileViewModel(f));
            PopulateChildren(items, token);
        }

        private async Task LoadFromRemoteAsync(string folderPath, System.Threading.CancellationToken token)
        {
            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            var provider = router.GetConnectionForPath(folderPath);
            if (provider == null)
            {
                IsLoading = false;
                ErrorMessage = "\uC6D0\uACA9 \uC5F0\uACB0\uC744 \uCC3E\uC744 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4";
                ErrorIcon = "\uE871";
                return;
            }

            try
            {
                var remotePath = FileSystemRouter.ExtractRemotePath(folderPath);
                var uriPrefix = FileSystemRouter.GetUriPrefix(folderPath);
                var remoteItems = await provider.GetItemsAsync(remotePath, token);

                var items = new List<FileSystemViewModel>();
                foreach (var item in remoteItems)
                {
                    if (token.IsCancellationRequested) break;
                    var fullPath = uriPrefix + item.Path;
                    if (item is FolderItem folder)
                    {
                        folder.Path = fullPath;
                        items.Add(new FolderViewModel(folder, _fileService));
                    }
                    else if (item is FileItem file)
                    {
                        file.Path = fullPath;
                        items.Add(new FileViewModel(file));
                    }
                }

                if (!token.IsCancellationRequested)
                    PopulateChildren(items, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 사용자가 명시적으로 취소한 경우만 재전파
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // 소켓 타임아웃 등 네트워크 레벨 취소 → 에러로 분류
                ErrorMessage = ClassifyRemoteError(ex);
                ErrorIcon = "\uE871";
            }
            catch (Exception ex)
            {
                ErrorMessage = ClassifyRemoteError(ex);
                ErrorIcon = "\uE871";
            }
        }

        private async Task LoadFromDiskAsync(
            string folderPath, bool showHidden,
            Services.FolderContentCache? folderCache,
            System.Threading.CancellationToken token)
        {
            var (items, rawFolders, rawFiles, errorMsg, errorIcon) = await Task.Run(() =>
            {
                var result = new List<FileSystemViewModel>();
                var folders = new List<Models.FolderItem>();
                var files = new List<Models.FileItem>();

                if (string.IsNullOrEmpty(folderPath))
                    return (result, folders, files, (string?)null, (string?)null);

                if (!System.IO.Directory.Exists(folderPath))
                {
                    // UNC 경로는 네트워크 문제와 경로 미존재를 구분
                    if (folderPath.StartsWith(@"\\"))
                        return (result, folders, files, "\uB124\uD2B8\uC6CC\uD06C \uACBD\uB85C\uC5D0 \uC811\uADFC\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4", "\uE871");
                    else
                        return (result, folders, files, "\uD3F4\uB354\uB97C \uCC3E\uC744 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4", "\uE711");
                }

                try
                {
                    var dirInfo = new System.IO.DirectoryInfo(folderPath);

                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        if (token.IsCancellationRequested) return (new List<FileSystemViewModel>(), folders, files, (string?)null, (string?)null);
                        if (!showHidden && (d.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((d.Attributes & System.IO.FileAttributes.System) != 0) continue;

                        var folderItem = new FolderItem { Name = d.Name, Path = d.FullName, DateModified = d.LastWriteTime, IsHidden = (d.Attributes & System.IO.FileAttributes.Hidden) != 0 };
                        folders.Add(folderItem);
                        result.Add(new FolderViewModel(folderItem, _fileService));
                    }

                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        if (token.IsCancellationRequested) return (new List<FileSystemViewModel>(), folders, files, (string?)null, (string?)null);
                        if (!showHidden && (f.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((f.Attributes & System.IO.FileAttributes.System) != 0) continue;

                        var fileItem = new FileItem { Name = f.Name, Path = f.FullName, Size = f.Length, DateModified = f.LastWriteTime, FileType = f.Extension, IsHidden = (f.Attributes & System.IO.FileAttributes.Hidden) != 0 };
                        files.Add(fileItem);
                        result.Add(new FileViewModel(fileItem));
                    }
                }
                catch (System.UnauthorizedAccessException)
                {
                    return (result, folders, files, "\uC811\uADFC\uC774 \uAC70\uBD80\uB418\uC5C8\uC2B5\uB2C8\uB2E4", "\uE72E");
                }
                catch (System.IO.PathTooLongException)
                {
                    return (result, folders, files, "\uACBD\uB85C\uAC00 \uB108\uBB34 \uAE41\uB2C8\uB2E4 (260\uC790 \uCD08\uACFC)", "\uE7BA");
                }
                catch (System.IO.DirectoryNotFoundException)
                {
                    return (result, folders, files, "\uD3F4\uB354\uB97C \uCC3E\uC744 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4", "\uE711");
                }
                catch (System.IO.IOException ex) when (ex.HResult is unchecked((int)0x80070035) or unchecked((int)0x8007052E))
                {
                    return (result, folders, files, "\uB124\uD2B8\uC6CC\uD06C \uC5F0\uACB0\uC774 \uB04A\uACBC\uC2B5\uB2C8\uB2E4", "\uE871");
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                    return (result, folders, files, $"\uB85C\uB4DC \uC2E4\uD328: {ex.Message}", "\uE783");
                }

                return (result, folders, files, (string?)null, (string?)null);
            }, token);

            if (!token.IsCancellationRequested)
            {
                if (errorMsg != null)
                {
                    ErrorMessage = errorMsg;
                    ErrorIcon = errorIcon;
                }
                else
                {
                    folderCache?.Set(folderPath, rawFolders, rawFiles, showHidden);
                }
                PopulateChildren(items, token);
            }
        }

        /// <summary>
        /// 인스턴스별 정렬 기준. 멀티윈도우 간 독립 정렬 지원.
        /// 초기값은 LocalSettings에서 복원 (앱 전체에서 한 번만 로드).
        /// </summary>
        private string _sortBy = s_defaultSortBy;
        private bool _sortAscending = s_defaultSortAscending;

        // 앱 전체 기본값 (LocalSettings에서 1회 로드)
        private static string s_defaultSortBy = "Name";
        private static bool s_defaultSortAscending = true;
        private static bool s_sortSettingsLoaded = false;

        /// <summary>
        /// 저장된 정렬 설정을 LocalSettings에서 복원 (최초 1회).
        /// 이후 생성되는 FolderViewModel의 기본 정렬값으로 사용.
        /// </summary>
        private static void EnsureSortSettingsLoaded()
        {
            if (s_sortSettingsLoaded) return;
            s_sortSettingsLoaded = true;
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("MillerSortBy", out var sortObj) && sortObj is string sortBy)
                    s_defaultSortBy = sortBy;
                if (settings.Values.TryGetValue("MillerSortAsc", out var ascObj) && ascObj is bool asc)
                    s_defaultSortAscending = asc;
                Helpers.DebugLogger.Log($"[FolderViewModel] Sort settings loaded: {s_defaultSortBy}, ascending={s_defaultSortAscending}");
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FolderViewModel] Sort settings load failed: {ex.Message}"); }
        }

        /// <summary>
        /// 정렬 기준 변경 후 현재 컬럼의 Children을 재정렬.
        /// </summary>
        public void SortChildren(string sortBy, bool ascending)
        {
            _sortBy = sortBy;
            _sortAscending = ascending;

            // _allChildren 기준 정렬 (있으면), 없으면 Children 기준
            var source = _allChildren ?? Children.ToList();
            if (source.Count == 0) return;
            IsSorting = true;
            _isBulkUpdating = true;
            var saved = SelectedChild;

            try
            {
                var sorted = ApplySort(source, sortBy, ascending);
                _allChildren = sorted;

                // 필터 활성 시 → 필터 재적용
                if (!string.IsNullOrEmpty(_currentFilterText))
                {
                    var filtered = sorted.Where(item => MatchesFilter(item.Name, _currentFilterText)).ToList();
                    Children = new ObservableCollection<FileSystemViewModel>(filtered);
                }
                else
                {
                    Children = new ObservableCollection<FileSystemViewModel>(sorted);
                }
                if (saved != null) SelectedChild = saved;
            }
            finally
            {
                IsSorting = false;
                _isBulkUpdating = false;
                // 정렬 완료 후 한 번에 갱신
                OnPropertyChanged(nameof(ChildCountText));
                OnPropertyChanged(nameof(TotalChildCount));
            }

            // 설정 저장 (다음 앱 실행의 기본값으로 사용)
            try
            {
                s_defaultSortBy = sortBy;
                s_defaultSortAscending = ascending;
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["MillerSortBy"] = sortBy;
                settings.Values["MillerSortAsc"] = ascending;
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FolderViewModel] Sort settings save failed: {ex.Message}"); }
        }

        private static List<FileSystemViewModel> ApplySort(
            List<FileSystemViewModel> items, string sortBy, bool ascending)
        {
            // 폴더/파일 1회 분할 — 비교마다 `x is FileViewModel` 타입 체크 제거 (14K+ 성능 최적화)
            var folders = new List<FileSystemViewModel>();
            var files = new List<FileSystemViewModel>();
            foreach (var item in items)
            {
                if (item is FileViewModel)
                    files.Add(item);
                else
                    folders.Add(item);
            }

            // 각 그룹 개별 정렬
            IEnumerable<FileSystemViewModel> sortedFolders, sortedFiles;
            switch (sortBy)
            {
                case "DateModified":
                    sortedFolders = ascending ? folders.OrderBy(x => x.DateModifiedValue) : folders.OrderByDescending(x => x.DateModifiedValue);
                    sortedFiles = ascending ? files.OrderBy(x => x.DateModifiedValue) : files.OrderByDescending(x => x.DateModifiedValue);
                    break;
                case "Type":
                    sortedFolders = ascending ? folders.OrderBy(x => x.FileType) : folders.OrderByDescending(x => x.FileType);
                    sortedFiles = ascending ? files.OrderBy(x => x.FileType) : files.OrderByDescending(x => x.FileType);
                    break;
                case "Size":
                    sortedFolders = ascending ? folders.OrderBy(x => x.SizeValue) : folders.OrderByDescending(x => x.SizeValue);
                    sortedFiles = ascending ? files.OrderBy(x => x.SizeValue) : files.OrderByDescending(x => x.SizeValue);
                    break;
                default: // "Name"
                    sortedFolders = ascending
                        ? folders.OrderBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                        : folders.OrderByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                    sortedFiles = ascending
                        ? files.OrderBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                        : files.OrderByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                    break;
            }

            // 폴더 먼저, 파일 나중
            var result = new List<FileSystemViewModel>(items.Count);
            result.AddRange(sortedFolders);
            result.AddRange(sortedFiles);
            return result;
        }

        /// <summary>
        /// Children 컬렉션에 정렬된 아이템을 채운다.
        /// 썸네일과 클라우드 상태는 on-demand (ContainerContentChanging)로 로드.
        /// 배치 교체로 CollectionChanged 이벤트를 최소화.
        /// </summary>
        private void PopulateChildren(List<FileSystemViewModel> items, System.Threading.CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            EnsureSortSettingsLoaded();
            // 인스턴스 정렬 기준이 기본값에서 갱신되지 않았다면 기본값 적용
            if (_sortBy == "Name" && s_defaultSortBy != "Name")
            {
                _sortBy = s_defaultSortBy;
                _sortAscending = s_defaultSortAscending;
            }
            var sortedItems = ApplySort(items, _sortBy, _sortAscending);

            // 클라우드 폴더 여부 캐시 (on-demand 주입용)
            try
            {
                _cloudSvc = App.Current.Services.GetService(typeof(CloudSyncService)) as CloudSyncService;
                _isCloudFolder = _cloudSvc != null && _cloudSvc.IsCloudPath(Path);
            }
            catch { _isCloudFolder = false; }

            // Git 레포 여부 캐시 (on-demand 주입용)
            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                Helpers.DebugLogger.Log($"[Git.Detect] Settings resolved={settings != null}, ShowGitIntegration={settings?.ShowGitIntegration}");
                if (settings != null && settings.ShowGitIntegration)
                {
                    _gitSvc = App.Current.Services.GetService(typeof(GitStatusService)) as GitStatusService;
                    var isAvail = _gitSvc?.IsAvailable == true;
                    var repoRoot = isAvail ? _gitSvc!.FindRepoRoot(Path) : null;
                    _isGitFolder = repoRoot != null;
                    Helpers.DebugLogger.Log($"[Git.Detect] GitSvc={_gitSvc != null}, IsAvailable={isAvail}, FindRepoRoot({Path})={repoRoot}, _isGitFolder={_isGitFolder}");
                }
                else
                {
                    _isGitFolder = false;
                    Helpers.DebugLogger.Log($"[Git.Detect] SKIPPED — setting off or null");
                }
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[Git.Detect] EXCEPTION: {ex.Message}"); _isGitFolder = false; }

            // _allChildren 저장 (필터 인프라)
            _allChildren = sortedItems;

            _isBulkUpdating = true;
            try
            {
                // 필터 활성 시 → 필터 적용, 아닐 때 → 전체 표시
                if (!string.IsNullOrEmpty(_currentFilterText))
                {
                    var filtered = sortedItems.Where(item => MatchesFilter(item.Name, _currentFilterText)).ToList();
                    Children = new ObservableCollection<FileSystemViewModel>(filtered);
                }
                else
                {
                    // 배치 교체: 1회 PropertyChanged("Children") → ListView 전체 리바인딩
                    Children = new ObservableCollection<FileSystemViewModel>(sortedItems);
                }

                Helpers.DebugLogger.Log($"[FolderViewModel] Children populated: {sortedItems.Count} items (batch), filtered={Children.Count}");
            }
            finally
            {
                _isBulkUpdating = false;
                OnPropertyChanged(nameof(ChildCountText));
                OnPropertyChanged(nameof(TotalChildCount));
            }

            // 썸네일은 ContainerContentChanging에서 on-demand 로드
            // (기존: 전체 순차 로드 제거)

            // Git 레포: 백그라운드에서 git status 실행 → 캐시 워밍 → UI 갱신
            if (_isGitFolder && _gitSvc != null)
            {
                _ = WarmGitCacheAsync(token);
            }
        }

        /// <summary>
        /// 백그라운드에서 git status 실행 → 캐시 채움 → UI 스레드에서 Children에 상태 주입.
        /// PopulateChildren에서 fire-and-forget으로 호출.
        /// </summary>
        private async Task WarmGitCacheAsync(CancellationToken ct)
        {
            // UI 스레드의 DispatcherQueue를 먼저 캡처
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Helpers.DebugLogger.Log($"[Git.Warm] START path={Path}, dispatcher={dispatcher != null}");

            try
            {
                // 백그라운드에서 git status 실행 → 캐시 채움
                Dictionary<string, Models.GitFileState>? states = null;
                await Task.Run(async () =>
                {
                    states = await _gitSvc!.GetFolderStatesAsync(Path, ct);
                }, ct);

                Helpers.DebugLogger.Log($"[Git.Warm] GetFolderStatesAsync returned {states?.Count ?? -1} entries");

                if (ct.IsCancellationRequested) return;

                // Git 캐시 워밍 완료 — 실제 UI 주입은 ContainerContentChanging에서 on-demand 수행.
                // 14K+ 파일 폴더에서 전체 Children 루프를 방지하여 UI 스레드 부하 제거.
                Helpers.DebugLogger.Log($"[Git.Warm] Cache warmed with {states?.Count ?? 0} entries, injection deferred to ContainerContentChanging");
            }
            catch (OperationCanceledException) { Helpers.DebugLogger.Log("[Git.Warm] Cancelled"); }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Git.Warm] ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// On-demand: 보이는 아이템에 클라우드 상태 주입.
        /// ContainerContentChanging에서 호출.
        /// </summary>
        public void InjectCloudStateIfNeeded(FileSystemViewModel item)
        {
            if (item.CloudStateInjected) return; // 이미 주입 완료 — 스크롤 시 재주입 방지
            if (!_isCloudFolder || _cloudSvc == null) { item.CloudStateInjected = true; return; }
            item.CloudState = _cloudSvc.GetCloudState(item.Path);
            item.CloudStateInjected = true;
        }

        /// <summary>
        /// On-demand: 보이는 아이템에 Git 상태 주입.
        /// ContainerContentChanging에서 호출 (Details 뷰 전용).
        /// 캐시된 값만 사용 (I/O 없음).
        /// </summary>
        public void InjectGitStateIfNeeded(FileSystemViewModel item)
        {
            if (item.GitStateInjected) return; // 이미 주입 완료 — 스크롤 시 재주입 방지
            if (!_isGitFolder || _gitSvc == null) { item.GitStateInjected = true; return; }
            var state = _gitSvc.GetCachedState(item.Path);
            if (state.HasValue)
                item.GitState = state.Value;
            item.GitStateInjected = true;
        }

        /// <summary>
        /// Git 레포 여부를 반환 (Details 뷰에서 상태 로드 판단용).
        /// </summary>
        public bool IsGitFolder => _isGitFolder;

        /// <summary>
        /// 원격 연결 예외를 사용자 친화적 에러 메시지로 분류.
        /// 연결 실패, 인증 실패, 타임아웃, 권한 거부를 구분.
        /// </summary>
        private static string ClassifyRemoteError(Exception ex)
        {
            var msg = ex.Message;
            var typeName = ex.GetType().Name;

            // 소켓/네트워크 레벨의 OperationCanceledException (사용자 취소가 아닌 타임아웃)
            if (ex is OperationCanceledException)
                return "연결 시간이 초과되었습니다: 서버 상태를 확인하세요";

            // SSH.NET 인증 실패
            if (typeName.Contains("Authentication") || msg.Contains("denied") || msg.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                return "인증 실패: 사용자 이름 또는 비밀번호를 확인하세요";

            // SSH.NET 연결 끊김
            if (typeName.Contains("SshConnection") || typeName.Contains("SshOperationTimeout"))
                return "서버 연결이 끊어졌습니다";

            // 소켓/네트워크 에러
            if (typeName.Contains("Socket") || msg.Contains("No such host")
                || msg.Contains("actively refused") || msg.Contains("Connection refused")
                || msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
                return "서버에 연결할 수 없습니다: 네트워크 상태를 확인하세요";

            // 타임아웃
            if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                return "연결 시간이 초과되었습니다: 서버 상태를 확인하세요";

            // FTP 권한 에러 (530, 550 등)
            if (msg.Contains("550") || msg.Contains("Permission denied")
                || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
                return "접근이 거부되었습니다";

            // FluentFTP 연결 실패
            if (msg.Contains("Unable to connect") || msg.Contains("disconnected")
                || msg.Contains("Not connected", StringComparison.OrdinalIgnoreCase))
                return "서버 연결에 실패했습니다";

            // 기본
            return $"원격 연결 오류: {msg}";
        }

        public void CancelLoading()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsLoading = false;

            // FolderSizeService 이벤트 구독 누수 방지
            UnsubscribeFolderSizeEvent();
        }

        /// <summary>
        /// FolderSizeService.SizeCalculated 이벤트 구독 해제.
        /// CancelLoading/ResetState에서 호출하여 메모리 누수 방지.
        /// </summary>
        private void UnsubscribeFolderSizeEvent()
        {
            try
            {
                var svc = App.Current.Services.GetService(typeof(FolderSizeService)) as FolderSizeService;
                if (svc != null) svc.SizeCalculated -= OnFolderSizeCalculated;
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FolderViewModel] FolderSize unsubscribe failed: {ex.Message}"); }
        }

        /// <summary>
        /// Set error state from NavigateIntoFolder exception (async void crash prevention).
        /// </summary>
        internal void SetNavigationError(string message)
        {
            ErrorMessage = message;
            ErrorIcon = "\uE783"; // generic error icon
        }

        /// <summary>
        /// Reset only the load flag and error state so the folder can be reloaded (retry button).
        /// </summary>
        public void ResetLoadState()
        {
            _isLoaded = false;
            ErrorMessage = null;
            ErrorIcon = null;
        }

        /// <summary>
        /// Reset folder state when removed from view.
        /// This ensures fresh data when folder is revisited.
        /// </summary>
        public void ResetState()
        {
            Helpers.DebugLogger.Log($"[FolderViewModel.ResetState] Resetting folder: {Name}");

            // Cancel any pending loading
            CancelLoading();

            // Clear selection to reset focus
            SelectedChild = null;

            // Release thumbnails to free memory
            UnloadAllThumbnails();

            // Clear filter state
            _allChildren = null;
            _currentFilterText = string.Empty;

            // Mark as not loaded so it reloads next time
            _isLoaded = false;

            Helpers.DebugLogger.Log($"[FolderViewModel.ResetState] Reset complete - _isLoaded=false, SelectedChild=null");
        }

        /// <summary>
        /// Release all thumbnails in this folder to free memory.
        /// Called when folder is removed from view or reset.
        /// </summary>
        public void UnloadAllThumbnails()
        {
            foreach (var child in Children)
            {
                if (child is FileViewModel fileVm && fileVm.ThumbnailSource != null)
                {
                    fileVm.UnloadThumbnail();
                }
            }
        }

        /// <summary>
        /// Fully release all child ViewModels and their resources.
        /// Used during tab close / window close to ensure GC can reclaim everything.
        /// Unlike UnloadAllThumbnails (which keeps Children for re-visit caching),
        /// this method clears the entire collection.
        /// </summary>
        public void ClearChildren()
        {
            UnloadAllThumbnails();
            Children = new System.Collections.ObjectModel.ObservableCollection<FileSystemViewModel>();
            _allChildren = null;
            _currentFilterText = string.Empty;
            _isLoaded = false;
        }

        /// <summary>
        /// 필터 텍스트를 적용하여 Children을 _allChildren의 부분집합으로 교체.
        /// 빈 문자열이면 전체 복원.
        /// </summary>
        public void ApplyFilter(string filterText)
        {
            _currentFilterText = filterText ?? string.Empty;
            _isBulkUpdating = true;

            try
            {
                if (_allChildren == null || _allChildren.Count == 0)
                    return;

                if (string.IsNullOrEmpty(_currentFilterText))
                {
                    Children = new ObservableCollection<FileSystemViewModel>(_allChildren);
                }
                else
                {
                    var filtered = _allChildren.Where(item => MatchesFilter(item.Name, _currentFilterText)).ToList();
                    Children = new ObservableCollection<FileSystemViewModel>(filtered);
                }
            }
            finally
            {
                _isBulkUpdating = false;
                // 필터 완료 후 한 번에 갱신
                OnPropertyChanged(nameof(ChildCountText));
                OnPropertyChanged(nameof(TotalChildCount));
            }
        }

        /// <summary>
        /// 이름이 필터 패턴에 매칭되는지 확인.
        /// - 빈 필터 → 항상 true
        /// - * 또는 ? 포함 → wildcard 패턴 매칭 (Regex 변환)
        /// - 기본 → 대소문자 무시 substring 매칭
        /// </summary>
        /// <summary>
        /// Compiled Regex cache for wildcard filter patterns.
        /// Avoids creating 14K+ Regex objects per filter application.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.RegularExpressions.Regex?> _regexCache = new();

        internal static bool MatchesFilter(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(name)) return false;

            if (filter.Contains('*') || filter.Contains('?'))
            {
                var regex = _regexCache.GetOrAdd(filter, f =>
                {
                    var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(f)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";
                    try
                    {
                        return new System.Text.RegularExpressions.Regex(
                            pattern,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                    catch { return null; }
                });

                return regex?.IsMatch(name) ?? false;
            }

            return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        // LoadThumbnailsAsync 제거됨 — 썸네일은 ContainerContentChanging에서 on-demand 로드

        /// <summary>
        /// Alias for ReloadAsync (used by settings refresh).
        /// </summary>
        public Task RefreshAsync() => ReloadAsync();

        /// <summary>
        /// Force reload (F5 새로고침).
        /// </summary>
        public async Task ReloadAsync()
        {
            Helpers.DebugLogger.Log($"[FolderViewModel.ReloadAsync] START - Folder: {Name}, Path: {Path}");

            // 캐시 무효화 (강제 새로고침)
            try
            {
                var cache = App.Current.Services.GetService(typeof(Services.FolderContentCache)) as Services.FolderContentCache;
                cache?.Invalidate(Path);
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FolderViewModel] Cache invalidate failed: {ex.Message}"); }

            _isLoaded = false;
            ErrorMessage = null;
            ErrorIcon = null;
            await EnsureChildrenLoadedAsync();

            Helpers.DebugLogger.Log($"[FolderViewModel.ReloadAsync] ===== COMPLETE =====");
        }

        partial void OnSelectedChildChanged(FileSystemViewModel? value)
        {
            // ExplorerViewModel listens to this via PropertyChanged
        }

        /// <summary>
        /// Synchronize SelectedItems from ListView.SelectionChanged.
        /// Single selection: updates SelectedChild (triggers auto-navigation).
        /// Multi-selection: suppresses SelectedChild update (navigation suppressed).
        /// </summary>
        public void SyncSelectedItems(IList<object> selectedObjects)
        {
            SelectedItems.Clear();
            foreach (var obj in selectedObjects)
            {
                if (obj is FileSystemViewModel fsvm)
                    SelectedItems.Add(fsvm);
            }

            // Single selection: sync SelectedChild for navigation
            if (SelectedItems.Count == 1)
            {
                SelectedChild = SelectedItems[0];
            }
            // Multi-selection: don't touch SelectedChild → navigation suppressed

            OnPropertyChanged(nameof(HasMultiSelection));
        }

        /// <summary>
        /// Get all selected items (multi or single).
        /// </summary>
        public List<FileSystemViewModel> GetSelectedItemsList()
        {
            if (HasMultiSelection)
                return SelectedItems.ToList();
            return SelectedChild != null ? new List<FileSystemViewModel> { SelectedChild } : new List<FileSystemViewModel>();
        }
    }
}
