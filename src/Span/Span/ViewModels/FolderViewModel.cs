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
            try
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                {
                    OnPropertyChanged(nameof(Size));
                    OnPropertyChanged(nameof(SizeValue));
                });
            }
            catch
            {
                OnPropertyChanged(nameof(Size));
                OnPropertyChanged(nameof(SizeValue));
            }
        }

        private static string FormatFolderSize(long bytes)
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

            if (Children.Count == 0) return;
            IsSorting = true;
            var saved = SelectedChild;

            try
            {
                var sorted = ApplySort(Children.ToList(), sortBy, ascending);
                Children = new ObservableCollection<FileSystemViewModel>(sorted);
                if (saved != null) SelectedChild = saved;
            }
            finally { IsSorting = false; }

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
            IEnumerable<FileSystemViewModel> sorted = sortBy switch
            {
                "DateModified" => ascending
                    ? items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.DateModifiedValue)
                    : items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.DateModifiedValue),
                "Type" => ascending
                    ? items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.FileType)
                    : items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.FileType),
                "Size" => ascending
                    ? items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.SizeValue)
                    : items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.SizeValue),
                _ => ascending
                    ? items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                    : items.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance),
            };
            return sorted.ToList();
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

            // 배치 교체: 1회 PropertyChanged("Children") → ListView 전체 리바인딩
            // (기존: 14,000회 CollectionChanged.Add 이벤트)
            Children = new ObservableCollection<FileSystemViewModel>(sortedItems);

            Helpers.DebugLogger.Log($"[FolderViewModel] Children populated: {sortedItems.Count} items (batch)");

            OnPropertyChanged(nameof(ChildCountText));

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

                // UI 스레드에서 Children에 Git 상태 주입
                if (dispatcher != null)
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        int injected = 0;
                        foreach (var child in Children)
                        {
                            if (ct.IsCancellationRequested) break;
                            var before = child.GitState;
                            InjectGitStateIfNeeded(child);
                            if (child.GitState != before) injected++;
                        }
                        Helpers.DebugLogger.Log($"[Git.Warm] UI inject done: {injected}/{Children.Count} items got git state");
                    });
                }
                else
                {
                    Helpers.DebugLogger.Log("[Git.Warm] WARNING: dispatcher is null, cannot inject on UI thread");
                }
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
            if (!_isCloudFolder || _cloudSvc == null) return;
            if (item.CloudState != CloudState.None) return;
            item.CloudState = _cloudSvc.GetCloudState(item.Path);
        }

        /// <summary>
        /// On-demand: 보이는 아이템에 Git 상태 주입.
        /// ContainerContentChanging에서 호출 (Details 뷰 전용).
        /// 캐시된 값만 사용 (I/O 없음).
        /// </summary>
        public void InjectGitStateIfNeeded(FileSystemViewModel item)
        {
            if (!_isGitFolder || _gitSvc == null) return;
            if (item.GitState != Models.GitFileState.None) return;
            var state = _gitSvc.GetCachedState(item.Path);
            if (state.HasValue)
                item.GitState = state.Value;
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
            _isLoaded = false;
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
