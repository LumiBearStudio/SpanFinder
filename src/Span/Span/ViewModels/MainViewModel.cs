using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System.Linq;
using System.Threading.Tasks;

namespace Span.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appTitle = "Span";

        public ObservableCollection<TabItem> Tabs { get; } = new();
        public ObservableCollection<DriveItem> Drives { get; } = new();

        // Engine
        private ExplorerViewModel _explorer;
        public ExplorerViewModel Explorer
        {
            get => _explorer;
            set => SetProperty(ref _explorer, value);
        }

        private readonly FileSystemService _fileService;
        private readonly FileOperationHistory _operationHistory;
        private readonly FileOperationProgressViewModel _progressViewModel;

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

        public FileOperationProgressViewModel ProgressViewModel => _progressViewModel;

        public MainViewModel(FileSystemService fileService)
        {
            _fileService = fileService;
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
        }

        private async void LoadDrives()
        {
            Drives.Clear();
            var drives = await _fileService.GetDrivesAsync();
            foreach (var drive in drives)
            {
                Drives.Add(drive);
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
    }
}
