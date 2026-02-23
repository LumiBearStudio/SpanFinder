using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Span.Models;
using System.Threading.Tasks;
using Span.Services;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Span.ViewModels
{
    public partial class FolderViewModel : FileSystemViewModel
    {
        private readonly FileSystemService _fileService;
        private readonly FolderItem _folderModel;
        private bool _isLoaded = false;
        private string? _calculatedSize;

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
                catch { }

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
            catch (TaskCanceledException) { }
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
                    IsEmpty = Children.Count == 0;
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
                IsEmpty = true;
                return;
            }

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

        private async Task LoadFromDiskAsync(
            string folderPath, bool showHidden,
            Services.FolderContentCache? folderCache,
            System.Threading.CancellationToken token)
        {
            var (items, rawFolders, rawFiles) = await Task.Run(() =>
            {
                var result = new List<FileSystemViewModel>();
                var folders = new List<Models.FolderItem>();
                var files = new List<Models.FileItem>();

                if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
                    return (result, folders, files);

                try
                {
                    var dirInfo = new System.IO.DirectoryInfo(folderPath);

                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        if (token.IsCancellationRequested) return (new List<FileSystemViewModel>(), folders, files);
                        if (!showHidden && (d.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((d.Attributes & System.IO.FileAttributes.System) != 0) continue;

                        var folderItem = new FolderItem { Name = d.Name, Path = d.FullName, DateModified = d.LastWriteTime };
                        folders.Add(folderItem);
                        result.Add(new FolderViewModel(folderItem, _fileService));
                    }

                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        if (token.IsCancellationRequested) return (new List<FileSystemViewModel>(), folders, files);
                        if (!showHidden && (f.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((f.Attributes & System.IO.FileAttributes.System) != 0) continue;

                        var fileItem = new FileItem { Name = f.Name, Path = f.FullName, Size = f.Length, DateModified = f.LastWriteTime, FileType = f.Extension };
                        files.Add(fileItem);
                        result.Add(new FileViewModel(fileItem));
                    }
                }
                catch (System.UnauthorizedAccessException) { }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                }

                return (result, folders, files);
            }, token);

            if (!token.IsCancellationRequested)
            {
                folderCache?.Set(folderPath, rawFolders, rawFiles, showHidden);
                PopulateChildren(items, token);
            }
        }

        /// <summary>
        /// Children 컬렉션에 정렬된 아이템을 채우고 썸네일 로드를 시작한다.
        /// 캐시 히트와 디스크 로드 양쪽에서 공통으로 사용.
        /// </summary>
        private void PopulateChildren(List<FileSystemViewModel> items, System.Threading.CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            var sortedItems = items
                .OrderBy(x => x is FileViewModel ? 1 : 0)
                .ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                .ToList();

            // 클라우드 동기화 상태 주입
            try
            {
                var cloudSvc = App.Current.Services.GetService(typeof(CloudSyncService)) as CloudSyncService;
                if (cloudSvc != null && cloudSvc.IsCloudPath(Path))
                {
                    foreach (var item in sortedItems)
                    {
                        item.CloudState = cloudSvc.GetCloudState(item.Path);
                    }
                }
            }
            catch { }

            Children.Clear();
            foreach (var item in sortedItems)
                Children.Add(item);

            Helpers.DebugLogger.Log($"[FolderViewModel] Children populated: {sortedItems.Count} items");

            OnPropertyChanged(nameof(ChildCountText));

            _ = LoadThumbnailsAsync(sortedItems, token);
        }

        public void CancelLoading()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsLoading = false;
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
        /// Load thumbnails for image files in the background.
        /// Processes in batches to avoid flooding the UI thread.
        /// </summary>
        private async Task LoadThumbnailsAsync(List<FileSystemViewModel> items, System.Threading.CancellationToken token)
        {
            try
            {
                var imageFiles = items.OfType<FileViewModel>()
                    .Where(f => f.IsThumbnailSupported)
                    .ToList();

                if (imageFiles.Count == 0) return;

                Helpers.DebugLogger.Log($"[FolderViewModel] Loading thumbnails for {imageFiles.Count} image files");

                foreach (var file in imageFiles)
                {
                    if (token.IsCancellationRequested) break;
                    await file.LoadThumbnailAsync();
                }
            }
            catch (System.Exception ex)
            {
                Helpers.DebugLogger.Log($"[FolderViewModel] Thumbnail batch load error: {ex.Message}");
            }
        }

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
            catch { }

            _isLoaded = false;
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
