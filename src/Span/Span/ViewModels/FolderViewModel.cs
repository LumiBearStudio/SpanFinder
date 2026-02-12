using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Span.Models;
using System.Threading.Tasks;
using Span.Services;

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

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isActive = false; // Indicates if this column has focus

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
            if (_isLoaded) return;
            _isLoaded = true;
            IsLoading = true;

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
                    // Ensure UI update happens on UI thread
                    var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    if (queue != null)
                    {
                        queue.TryEnqueue(() =>
                        {
                            if (!token.IsCancellationRequested)
                                Children = new ObservableCollection<FileSystemViewModel>(items);
                        });
                    }
                    else
                    {
                        // Fallback if queue is null (e.g. unit test or direct call), though less safe for UI binding
                        Children = new ObservableCollection<FileSystemViewModel>(items);
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoading = false;
                }
                if (_cts?.Token == token)
                {
                    _cts.Dispose();
                    _cts = null;
                }
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
        /// Force reload (F5 새로고침).
        /// </summary>
        public async Task ReloadAsync()
        {
            _isLoaded = false;
            await EnsureChildrenLoadedAsync();
        }

        partial void OnSelectedChildChanged(FileSystemViewModel? value)
        {
            // ExplorerViewModel listens to this via PropertyChanged
        }
    }
}
