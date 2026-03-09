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

        /// <summary>
        /// 사이드바/Home 화면에서 드라이브 클릭 시 해당 드라이브로 탐색.
        /// Home/ActionLog 모드인 경우 이전 뷰모드(Details/List/Icon 등)를 복원하여
        /// 사용자가 Home 전환 전에 사용하던 뷰를 유지함.
        /// </summary>
        [RelayCommand]
        public void OpenDrive(DriveItem drive)
        {
            // Home/ActionLog에서 벗어나되, 탐색기 뷰모드(Details/List/Icon)는 보존.
            // ResolveViewModeFromHome()이 _lastClosedViewMode → _viewModeBeforeHome → Miller 순으로 결정.
            var activeViewMode = (IsSplitViewEnabled && ActivePane == ActivePane.Right)
                ? RightViewMode : CurrentViewMode;
            Helpers.DebugLogger.Log($"[OpenDrive] activeViewMode={activeViewMode}, CurrentViewMode={CurrentViewMode}");
            if (activeViewMode == ViewMode.Home || activeViewMode == ViewMode.ActionLog)
            {
                var resolved = ResolveViewModeFromHome();
                Helpers.DebugLogger.Log($"[OpenDrive] Home→Drive: switching to {resolved}");
                SwitchViewMode(resolved);
            }
            Helpers.DebugLogger.Log($"[OpenDrive] AFTER switch: CurrentViewMode={CurrentViewMode}");

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
            var desc = UndoDescription;
            var result = await _operationHistory.UndoAsync();
            _actionLogService.LogOperation(new Models.ActionLogEntry
            {
                OperationType = "Undo",
                Description = desc ?? _loc.Get("LogUndo"),
                Success = result.Success,
                ErrorMessage = result.ErrorMessage
            });
            if (result.Success)
            {
                await RefreshCurrentFolderAsync();
                await RefreshOppositeExplorerAsync();
                ShowToast(string.Format(_loc.Get("Toast_Undone"), desc));
            }
            else
            {
                ShowError(result.ErrorMessage ?? _loc.Get("Toast_UndoFailed"));
            }
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private async Task RedoAsync()
        {
            var desc = RedoDescription;
            var result = await _operationHistory.RedoAsync();
            _actionLogService.LogOperation(new Models.ActionLogEntry
            {
                OperationType = "Redo",
                Description = desc ?? _loc.Get("LogRedo"),
                Success = result.Success,
                ErrorMessage = result.ErrorMessage
            });
            if (result.Success)
            {
                await RefreshCurrentFolderAsync();
                await RefreshOppositeExplorerAsync();
                ShowToast(string.Format(_loc.Get("Toast_Redone"), desc));
            }
            else
            {
                ShowError(result.ErrorMessage ?? _loc.Get("Toast_RedoFailed"));
            }
        }

        public async Task ExecuteFileOperationAsync(IFileOperation operation, int? targetColumnIndex = null)
        {
            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] START - Operation: {operation.Description}, TargetColumnIndex: {targetColumnIndex}");
            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] Columns: {string.Join(" > ", ActiveExplorer?.Columns?.Select(c => c.Name) ?? Array.Empty<string>())}");

            // Copy/Move operations go through the FileOperationManager for concurrent execution
            // with pause/resume/cancel support. Other operations use the legacy synchronous path.
            if (operation is CopyFileOperation or MoveFileOperation or CompressOperation or ExtractOperation)
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
                await RefreshOppositeExplorerAsync();
                Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] RefreshCurrentFolderAsync completed");

                if (operation.CanUndo)
                {
                    ShowToast(string.Format(_loc.Get("Toast_CompletedUndo"), operation.Description));
                }
                else
                {
                    ShowToast(string.Format(_loc.Get("Toast_Completed"), operation.Description));
                }
            }
            else
            {
                ShowError(result.ErrorMessage ?? _loc.Get("Toast_OperationFailed"));
            }

            Helpers.DebugLogger.Log($"[ExecuteFileOperationAsync] ===== COMPLETE =====");
        }

        /// <summary>
        /// Copy/Move 작업을 FileOperationManager를 통해 백그라운드에서 동시 실행.
        /// 일시정지(Pause)/재개(Resume)/취소(Cancel) 지원.
        /// UI 스레드를 차단하지 않으며, 완료 시 DispatcherQueue로 콜백하여 결과 처리.
        /// Undo 지원: 성공 시 CompletedOperationWrapper로 히스토리에 추가 (Ctrl+Z 가능).
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
                    try
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
                        await RefreshOppositeExplorerAsync();
                        ShowToast(string.Format(_loc.Get("Toast_Completed"), operation.Description));
                    }
                    else if (e.Entry.Status != Services.OperationStatus.Cancelled)
                    {
                        ShowError(e.Result.ErrorMessage ?? _loc.Get("Toast_OperationFailed"));
                    }
                    }
                    catch (Exception ex) { Helpers.DebugLogger.Log($"[FileOps] Post-operation dispatch failed: {ex.Message}"); }
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

        /// <summary>
        /// 마지막 명시적 리프레시 시각. FileWatcher 디바운싱에 사용.
        /// </summary>
        public DateTime LastExplicitRefreshTime { get; internal set; }

        /// <summary>
        /// Split View 활성 시 반대쪽 패널의 모든 컬럼을 리프레시.
        /// Copy/Move/Undo/Redo 완료 후 소스·대상 양쪽이 모두 최신 상태를 반영하도록 한다.
        /// columnIndex=0으로 호출하면 cascade 로직에 의해 모든 후속 컬럼도 리프레시됨.
        /// </summary>
        private async Task RefreshOppositeExplorerAsync()
        {
            if (!IsSplitViewEnabled) return;

            var opposite = ActivePane == ActivePane.Left ? RightExplorer : LeftExplorer;
            if (opposite?.Columns == null || opposite.Columns.Count == 0) return;

            Helpers.DebugLogger.Log($"[RefreshOppositeExplorer] Refreshing ALL columns of opposite pane ({(ActivePane == ActivePane.Left ? "Right" : "Left")})");
            // Refresh from column 0 → cascade reloads all subsequent columns too
            await RefreshCurrentFolderAsync(0, opposite);
        }

        public async Task RefreshCurrentFolderAsync(int? columnIndex = null, ExplorerViewModel? explorer = null)
        {
            explorer ??= ActiveExplorer;
            LastExplicitRefreshTime = DateTime.UtcNow;
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] START - columnIndex: {columnIndex}");

            if (explorer?.Columns == null || explorer.Columns.Count == 0)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] No columns to refresh - ABORT");
                return;
            }

            // Determine which column to refresh
            int targetIndex = columnIndex ?? explorer.Columns.Count - 1;
            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Target index: {targetIndex} (total columns: {explorer.Columns.Count})");

            if (targetIndex < 0 || targetIndex >= explorer.Columns.Count)
            {
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Invalid index - ABORT");
                return;
            }

            // Reload target column and all subsequent columns (cascade).
            // ReloadAsync internally uses SyncChildren (diff-based incremental update)
            // which preserves existing ViewModel instances → selection/scroll/thumbnail state kept.
            // DO NOT clear SelectedChild — SyncChildren retains matching instances by Path.
            int lastIndex = explorer.Columns.Count - 1;
            for (int i = targetIndex; i <= lastIndex; i++)
            {
                var col = explorer.Columns[i];
                Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] Reloading column '{col.Name}' (index {i})");
                await col.ReloadAsync();
            }

            // Notify ExplorerViewModel so Details/List/Icon views rebind
            explorer.NotifyCurrentItemsChanged();

            Helpers.DebugLogger.Log($"[RefreshCurrentFolderAsync] ===== COMPLETE ({lastIndex - targetIndex + 1} column(s)) =====");
        }

        #endregion

        #region Toast / Notifications

        public void ShowToast(string message, int durationMs = 3000, bool isError = false)
        {
            _toastTimer?.Dispose();
            ToastMessage = message;
            IsToastError = isError;
            IsToastVisible = true;

            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _toastTimer = new System.Threading.Timer(_ =>
            {
                if (dq != null)
                    dq.TryEnqueue(() => { IsToastVisible = false; });
                else
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
