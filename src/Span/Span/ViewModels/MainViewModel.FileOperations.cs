using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Span.ViewModels
{
    /// <summary>
    /// MainViewModel partial — 파일 조작 실행 (Copy/Move/Delete/Rename 등),
    /// FileOperationHistory Undo/Redo, FileOperationManager 연동(동시 실행/일시정지/취소),
    /// ActionLog 기록, 토스트 알림 처리.
    /// </summary>
    public partial class MainViewModel
    {
        #region File Operations

        [RelayCommand]
        public void OpenDrive(DriveItem drive)
        {
            // Switch away from Home mode if needed (same pattern as NavigateToFavorite)
            var activeViewMode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            if (activeViewMode == ViewMode.Home || activeViewMode == ViewMode.ActionLog)
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
                ItemCount = result.AffectedPaths.Count,
                DestinationPath = operation switch
                {
                    CopyFileOperation copyOp => copyOp.DestinationDirectory,
                    MoveFileOperation moveOp => moveOp.DestinationDirectory,
                    _ => null
                }
            });
        }

        public async Task RefreshCurrentFolderAsync(int? columnIndex = null, ExplorerViewModel? explorer = null)
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

            // Explicitly notify ExplorerViewModel so Details/List/Icon views rebind
            explorer.NotifyCurrentItemsChanged();

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

        #endregion

        #region Toast / Notifications

        public void ShowToast(string message, int durationMs = 3000, bool isError = false)
        {
            _toastTimer?.Dispose();
            ToastMessage = message;
            IsToastError = isError;
            IsToastVisible = true;

            _toastTimer = new System.Threading.Timer(_ =>
            {
                IsToastVisible = false;
            }, null, durationMs, System.Threading.Timeout.Infinite);
        }

        public void ShowError(string message)
        {
            ShowToast(message, 5000, isError: true);
        }

        #endregion
    }
}
