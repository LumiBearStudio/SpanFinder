using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Span.Models;
using System.Threading.Tasks;
using Span.Services;
using System.Linq;

namespace Span.ViewModels
{
    public partial class FolderViewModel : FileSystemViewModel
    {
        private readonly FileSystemService _fileService;
        private readonly FolderItem _folderModel;
        private bool _isLoaded = false;

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

        [ObservableProperty]
        private bool _isActive = false; // Indicates if this column has focus

        /// <summary>
        /// 정렬 중 플래그 - true일 때 PropertyChanged 이벤트 무시
        /// </summary>
        public bool IsSorting { get; set; } = false;

        public override string IconGlyph => Services.IconService.Current.FolderIcon;
        public override Microsoft.UI.Xaml.Media.Brush IconBrush => Services.IconService.Current.FolderBrush;

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
            Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] START - Folder: {Name}, _isLoaded: {_isLoaded}");

            if (_isLoaded)
            {
                Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] Already loaded - RETURN");
                return;
            }

            _isLoaded = true;
            IsLoading = true;

            Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] Loading children from disk...");

            _cts?.Cancel();
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var items = await Task.Run(() =>
                {
                    var result = new List<FileSystemViewModel>();
                    var path = _folderModel.Path;

                    if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
                        return result;

                    try
                    {
                        var dirInfo = new System.IO.DirectoryInfo(path);

                        foreach (var d in dirInfo.EnumerateDirectories())
                        {
                            if (token.IsCancellationRequested) return new List<FileSystemViewModel>();
                            if ((d.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                            if ((d.Attributes & System.IO.FileAttributes.System) != 0) continue;

                            result.Add(new FolderViewModel(
                                new FolderItem { Name = d.Name, Path = d.FullName, DateModified = d.LastWriteTime },
                                _fileService
                            ));
                        }

                        foreach (var f in dirInfo.EnumerateFiles())
                        {
                            if (token.IsCancellationRequested) return new List<FileSystemViewModel>();
                            if ((f.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                            if ((f.Attributes & System.IO.FileAttributes.System) != 0) continue;

                            result.Add(new FileViewModel(
                                new FileItem { Name = f.Name, Path = f.FullName, Size = f.Length, DateModified = f.LastWriteTime, FileType = f.Extension }
                            ));
                        }
                    }
                    catch (System.UnauthorizedAccessException) { }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                    }

                    return result;
                }, token);

                if (!token.IsCancellationRequested)
                {
                    // Update Children synchronously - we're already on UI thread via await
                    // CRITICAL FIX: Clear and re-add instead of replacing entire collection
                    // This ensures UI bindings update correctly
                    Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] Updating Children: {Children.Count} → {items.Count}");

                    // Apply natural sorting: folders first, then files, both sorted naturally
                    var sortedItems = items
                        .OrderBy(x => x is FileViewModel ? 1 : 0)  // Folders first
                        .ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                        .ToList();

                    Children.Clear();
                    foreach (var item in sortedItems)
                    {
                        Children.Add(item);
                    }

                    Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] Children updated: {sortedItems.Count} items loaded (naturally sorted)");

                    // Load thumbnails for image files in the background
                    _ = LoadThumbnailsAsync(sortedItems, token);
                }
                else
                {
                    Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] Cancelled before updating Children");
                }
            }
            catch (TaskCanceledException)
            {
                Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] TaskCanceledException caught");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoading = false;
                    Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] IsLoading = false");
                }
                if (_cts?.Token == token)
                {
                    _cts.Dispose();
                    _cts = null;
                }
                Helpers.DebugLogger.Log($"[FolderViewModel.EnsureChildrenLoadedAsync] ===== COMPLETE =====");
            }
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

            // Mark as not loaded so it reloads next time
            _isLoaded = false;

            Helpers.DebugLogger.Log($"[FolderViewModel.ResetState] Reset complete - _isLoaded=false, SelectedChild=null");
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
            Helpers.DebugLogger.Log($"[FolderViewModel.ReloadAsync] Children before reload: {Children.Count}");

            _isLoaded = false;
            Helpers.DebugLogger.Log($"[FolderViewModel.ReloadAsync] _isLoaded set to false, calling EnsureChildrenLoadedAsync()...");

            await EnsureChildrenLoadedAsync();

            Helpers.DebugLogger.Log($"[FolderViewModel.ReloadAsync] Children after reload: {Children.Count}");
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
