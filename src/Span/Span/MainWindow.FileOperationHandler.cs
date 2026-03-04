using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using Span.Views.Dialogs;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// MainWindow의 파일 작업 처리 부분 클래스.
    /// 선택 작업(전체 선택, 선택 해제, 반전), 복사/잘라내기/붙여넣기,
    /// 새 폴더/파일 생성, 이름 변경, 삭제, 압축/해제 등
    /// 모든 파일 시스템 작업의 UI 연동 로직을 담당한다.
    /// 충돌 처리 대화상자 표시와 <see cref="FileOperationManager"/>를 통한 작업 실행을 포함한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        #region Selection Operations (SelectAll, SelectNone, InvertSelection)

        /// <summary>
        /// 전체 선택 처리 (Ctrl+A).
        /// SplitView 활성 시 ActivePane에 따른 뷰 모드를 확인하고,
        /// MillerColumns/Details/List/Icon 모드 각각에 맞는 뷰의 SelectAll을 호출한다.
        /// Miller 모드에서는 포커스가 없는 경우 마지막 컬럼을 대상으로 한다.
        /// </summary>
        private void HandleSelectAll()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                var listView = GetListViewForColumn(activeIndex);
                listView?.SelectAll();
            }
            else if (viewMode == ViewMode.Details)
            {
                GetActiveDetailsView()?.SelectAll();
            }
            else if (viewMode == ViewMode.List)
            {
                GetActiveListView()?.SelectAll();
            }
            else if (Helpers.ViewModeExtensions.IsIconMode(viewMode))
            {
                GetActiveIconView()?.SelectAll();
            }
        }

        // =================================================================
        //  Select None (Ctrl+Shift+A)
        // =================================================================

        /// <summary>
        /// 선택 해제 처리. 현재 활성 뷰의 모든 선택을 해제하고
        /// FolderViewModel의 선택 상태를 초기화한다.
        /// </summary>
        private void HandleSelectNone()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                if (activeIndex < 0) return;

                var listView = GetListViewForColumn(activeIndex);
                if (listView != null)
                {
                    listView.SelectedItems.Clear();
                    // Also clear the ViewModel selection
                    var columns = ViewModel.ActiveExplorer.Columns;
                    if (activeIndex < columns.Count)
                    {
                        columns[activeIndex].SelectedChild = null;
                        columns[activeIndex].SelectedItems.Clear();
                    }
                }
            }
            else if (viewMode == ViewMode.Details)
            {
                GetActiveDetailsView()?.SelectNone();
            }
            else if (viewMode == ViewMode.List)
            {
                GetActiveListView()?.SelectNone();
            }
            else if (Helpers.ViewModeExtensions.IsIconMode(viewMode))
            {
                GetActiveIconView()?.SelectNone();
            }
        }

        // =================================================================
        //  Invert Selection (Ctrl+I)
        // =================================================================

        /// <summary>
        /// 선택 반전 처리. 현재 선택된 항목을 해제하고, 선택되지 않은 항목을 선택한다.
        /// </summary>
        private void HandleInvertSelection()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                if (activeIndex < 0) return;

                var listView = GetListViewForColumn(activeIndex);
                if (listView == null) return;

                var columns = ViewModel.ActiveExplorer.Columns;
                if (activeIndex >= columns.Count) return;

                var column = columns[activeIndex];
                var allItems = column.Children.ToList();

                // Collect currently selected indices
                var selectedIndices = new HashSet<int>();
                foreach (var item in listView.SelectedItems)
                {
                    int idx = allItems.IndexOf(item as FileSystemViewModel);
                    if (idx >= 0) selectedIndices.Add(idx);
                }

                // Clear and invert
                _isSyncingSelection = true;
                try
                {
                    listView.SelectedItems.Clear();
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        if (!selectedIndices.Contains(i))
                        {
                            listView.SelectedItems.Add(allItems[i]);
                        }
                    }
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
            else if (viewMode == ViewMode.Details)
            {
                GetActiveDetailsView()?.InvertSelection();
            }
            else if (viewMode == ViewMode.List)
            {
                GetActiveListView()?.InvertSelection();
            }
            else if (Helpers.ViewModeExtensions.IsIconMode(viewMode))
            {
                GetActiveIconView()?.InvertSelection();
            }

            ViewModel.UpdateStatusBar();
        }

        // =================================================================
        //  Helper: Get current selected items (multi or single)
        // =================================================================

        /// <summary>
        /// 현재 활성 뷰에서 선택된 항목 목록을 반환한다.
        /// 다중 선택이 있으면 다중 선택 항목을, 없으면 단일 선택 항목을 반환한다.
        /// </summary>
        private List<FileSystemViewModel> GetCurrentSelectedItems()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode != ViewMode.MillerColumns)
            {
                // Details/List/Icon: CurrentFolder에서 선택된 항목을 가져옴
                var currentFolder = ViewModel.ActiveExplorer.CurrentFolder;
                if (currentFolder != null)
                    return currentFolder.GetSelectedItemsList();
                return new List<FileSystemViewModel>();
            }

            // Miller Columns: 활성 컬럼에서 선택된 항목을 가져옴
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return new List<FileSystemViewModel>();

            var col = columns[activeIndex];
            return col.GetSelectedItemsList();
        }

        /// <summary>
        /// 현재 뷰 모드에 맞는 활성 FolderViewModel을 반환한다.
        /// Miller: 활성 컬럼, non-Miller: CurrentFolder.
        /// </summary>
        private FolderViewModel? GetCurrentViewFolder()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode != ViewMode.MillerColumns)
                return ViewModel.ActiveExplorer.CurrentFolder;

            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return null;
            return columns[activeIndex];
        }

        #endregion

        #region Clipboard Operations (Copy, Cut, Paste)

        /// <summary>
        /// 복사 작업 처리 (Ctrl+C).
        /// 선택된 항목의 경로를 내부 _clipboardPaths에 저장하고 _isCutOperation=false로 설정한다.
        /// 시스템 클립보드에 StorageItems도 제공하여 Windows 탐색기와의 호환성을 보장한다.
        /// </summary>
        private void HandleCopy()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                // Fallback: auto-select first item if nothing is selected
                var folder = GetCurrentViewFolder();
                if (folder != null && folder.Children.Count > 0)
                {
                    folder.SelectedChild = folder.Children[0];
                    selectedItems = new List<FileSystemViewModel> { folder.Children[0] };
                }
            }
            if (selectedItems.Count == 0) return;

            _clipboardPaths.Clear();
            foreach (var item in selectedItems)
                _clipboardPaths.Add(item.Path);
            _isCutOperation = false;

            var dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText(string.Join("\n", _clipboardPaths));

            // Provide StorageItems for Windows Explorer compatibility
            var capturedPaths = new List<string>(_clipboardPaths);
            dataPackage.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = Helpers.ViewDragDropHelper.ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });

            Clipboard.SetContent(dataPackage);

            // Toast notification
            if (selectedItems.Count == 1)
            {
                var name = System.IO.Path.GetFileName(selectedItems[0].Path);
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_Copied"), name));
            }
            else
            {
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_CopiedMultiple"), selectedItems.Count));
            }

            Helpers.DebugLogger.Log($"[Clipboard] Copied {_clipboardPaths.Count} item(s)");
            UpdateToolbarButtonStates();
        }

        /// <summary>
        /// 잘라내기 작업 처리 (Ctrl+X).
        /// HandleCopy와 동일한 흐름이지만 _isCutOperation=true로 설정하고,
        /// DataPackage.RequestedOperation을 Move로 지정하여 붙여넣기 시 이동 동작을 수행한다.
        /// </summary>
        private void HandleCut()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var folder = GetCurrentViewFolder();
                if (folder != null && folder.Children.Count > 0)
                {
                    folder.SelectedChild = folder.Children[0];
                    selectedItems = new List<FileSystemViewModel> { folder.Children[0] };
                }
            }
            if (selectedItems.Count == 0) return;

            _clipboardPaths.Clear();
            foreach (var item in selectedItems)
                _clipboardPaths.Add(item.Path);
            _isCutOperation = true;

            var dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Move;
            dataPackage.SetText(string.Join("\n", _clipboardPaths));

            // Provide StorageItems for Windows Explorer compatibility
            var capturedCutPaths = new List<string>(_clipboardPaths);
            dataPackage.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = Helpers.ViewDragDropHelper.ProvideStorageItemsAsync(request, capturedCutPaths, deferral);
            });

            Clipboard.SetContent(dataPackage);

            // Toast notification
            if (selectedItems.Count == 1)
            {
                var name = System.IO.Path.GetFileName(selectedItems[0].Path);
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_Cut"), name));
            }
            else
            {
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_CutMultiple"), selectedItems.Count));
            }

            Helpers.DebugLogger.Log($"[Clipboard] Cut {_clipboardPaths.Count} item(s)");
            UpdateToolbarButtonStates();
        }

        /// <summary>
        /// 붙여넣기 작업 처리 (Ctrl+V).
        /// 대상 디렉토리(destDir)는 현재 뷰 모드에 따라 결정된다:
        /// - 비-Miller 모드: CurrentFolder 경로 사용
        /// - Miller 모드: GetActiveColumnIndex()로 포커스된 컬럼의 경로 사용 (포커스 없으면 마지막 컬럼)
        /// 내부 클립보드(_clipboardPaths)와 외부 클립보드(Windows StorageItems) 모두 지원한다.
        /// 충돌 시 ConflictResolutionDialog를 표시하여 사용자 선택을 받는다.
        /// </summary>
        private async void HandlePaste()
        {
            try
            {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            FolderViewModel? targetFolder;
            int activeIndex;

            string destDir;

            Helpers.DebugLogger.Log($"[HandlePaste] viewMode={viewMode}, ColumnsCount={ViewModel.ActiveExplorer.Columns.Count}");
            for (int dbgI = 0; dbgI < ViewModel.ActiveExplorer.Columns.Count; dbgI++)
            {
                var dbgCol = ViewModel.ActiveExplorer.Columns[dbgI];
                Helpers.DebugLogger.Log($"[HandlePaste]   Column[{dbgI}]: Path={dbgCol.Path}, SelectedChild={dbgCol.SelectedChild?.Name ?? "null"}");
            }

            if (viewMode != ViewMode.MillerColumns)
            {
                targetFolder = ViewModel.ActiveExplorer.CurrentFolder;
                activeIndex = -1; // non-Miller: activeIndex 불필요
                if (targetFolder == null) return;
                destDir = targetFolder.Path;
                Helpers.DebugLogger.Log($"[HandlePaste] Non-Miller: destDir={destDir}");
            }
            else
            {
                var columns = ViewModel.ActiveExplorer.Columns;
                activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                Helpers.DebugLogger.Log($"[HandlePaste] Miller: GetActiveColumnIndex={activeIndex}, columns.Count={columns.Count}");
                if (activeIndex < 0 || activeIndex >= columns.Count) return;

                var col = columns[activeIndex];
                destDir = col.Path;
                targetFolder = col;
                Helpers.DebugLogger.Log($"[HandlePaste] FINAL destDir={destDir}");
            }

            List<string> sourcePaths;
            bool isCut;

            Helpers.DebugLogger.Log($"[HandlePaste] _clipboardPaths.Count={_clipboardPaths.Count}, _isCutOperation={_isCutOperation}");
            if (_clipboardPaths.Count > 0)
            {
                // Internal clipboard (Span → Span copy/cut)
                sourcePaths = new List<string>(_clipboardPaths);
                isCut = _isCutOperation;
            }
            else
            {
                // External clipboard (Windows Explorer → Span)
                try
                {
                    var content = Clipboard.GetContent();
                    if (!content.Contains(StandardDataFormats.StorageItems)) return;

                    // Bug 1: 클립보드 접근에 타임아웃 적용 (COM 교착 방지)
                    var clipTask = content.GetStorageItemsAsync().AsTask();
                    if (await Task.WhenAny(clipTask, Task.Delay(5000)) != clipTask)
                    {
                        Helpers.DebugLogger.Log("[Clipboard] GetStorageItemsAsync timed out (5s)");
                        return;
                    }
                    var items = clipTask.Result;
                    sourcePaths = items
                        .Select(i => i.Path)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                    if (sourcePaths.Count == 0) return;

                    // Detect Cut vs Copy from Windows clipboard
                    isCut = content.RequestedOperation.HasFlag(DataPackageOperation.Move);

                    Helpers.DebugLogger.Log($"[Clipboard] External paste: {sourcePaths.Count} item(s), isCut={isCut}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Clipboard] External paste error: {ex.Message}");
                    return;
                }
            }

            // 자기 폴더 복사 방지: 폴더를 자기 자신 안에 복사/이동하면 무한 재귀 발생
            var destNorm = destDir.TrimEnd('\\', '/') + "\\";
            int removedCount = sourcePaths.RemoveAll(srcPath =>
            {
                if (Directory.Exists(srcPath))
                {
                    var srcNorm = srcPath.TrimEnd('\\', '/') + "\\";
                    if (destNorm.StartsWith(srcNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        Helpers.DebugLogger.Log($"[Paste] 자기 복사 차단: {srcPath} → {destDir}");
                        return true;
                    }
                }
                return false;
            });
            if (sourcePaths.Count == 0)
            {
                if (removedCount > 0)
                {
                    var loc = App.Current.Services.GetRequiredService<LocalizationService>();
                    ViewModel.ShowToast(loc.Get("CannotCopyToSelf") ?? "폴더를 자기 자신 안에 복사할 수 없습니다.", 3000, isError: true);
                }
                return;
            }

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();

            // Pre-check for conflicts (local destinations only)
            bool destIsRemote = FileSystemRouter.IsRemotePath(destDir);
            ConflictResolution resolution = ConflictResolution.KeepBoth;
            bool applyToAll = true;
            bool hasConflicts = false;

            if (!destIsRemote)
            {
                string? firstConflictSrc = null;
                string? firstConflictDest = null;

                foreach (var srcPath in sourcePaths)
                {
                    var fileName = System.IO.Path.GetFileName(srcPath);
                    var destPath = System.IO.Path.Combine(destDir, fileName);
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        // Skip self-copy (same path)
                        if (string.Equals(srcPath, destPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        hasConflicts = true;
                        firstConflictSrc ??= srcPath;
                        firstConflictDest ??= destPath;
                    }
                }

                if (hasConflicts && firstConflictSrc != null && firstConflictDest != null)
                {
                    var vm = new FileConflictDialogViewModel
                    {
                        SourcePath = firstConflictSrc,
                        DestinationPath = firstConflictDest,
                    };

                    // Populate file info
                    try
                    {
                        if (File.Exists(firstConflictSrc))
                        {
                            var srcInfo = new FileInfo(firstConflictSrc);
                            vm.SourceSize = srcInfo.Length;
                            vm.SourceModified = srcInfo.LastWriteTime;
                        }
                        else if (Directory.Exists(firstConflictSrc))
                        {
                            vm.SourceModified = new DirectoryInfo(firstConflictSrc).LastWriteTime;
                        }

                        if (File.Exists(firstConflictDest))
                        {
                            var dstInfo = new FileInfo(firstConflictDest);
                            vm.DestinationSize = dstInfo.Length;
                            vm.DestinationModified = dstInfo.LastWriteTime;
                        }
                        else if (Directory.Exists(firstConflictDest))
                        {
                            vm.DestinationModified = new DirectoryInfo(firstConflictDest).LastWriteTime;
                        }
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[Clipboard] Conflict info error: {ex.Message}");
                    }

                    var dialog = new FileConflictDialog(vm);
                    dialog.XamlRoot = this.Content.XamlRoot;

                    var dialogResult = await ShowContentDialogSafeAsync(dialog);
                    if (_isClosed) return; // 다이얼로그 표시 중 창 닫힘 방어 (HandleDelete 패턴과 동일)
                    if (dialogResult != ContentDialogResult.Primary)
                    {
                        Helpers.DebugLogger.Log("[Clipboard] Paste cancelled by user (conflict dialog)");
                        return;
                    }

                    resolution = vm.SelectedResolution;
                    applyToAll = true; // Apply chosen resolution to all conflicts
                    Helpers.DebugLogger.Log($"[Clipboard] Conflict resolution: {resolution}, ApplyToAll: {applyToAll}");
                }
            }

            Helpers.DebugLogger.Log($"[HandlePaste] isCut={isCut} → {(isCut ? "MoveFileOperation" : "CopyFileOperation")}");
            Span.Services.FileOperations.IFileOperation op;
            if (isCut)
            {
                var moveOp = new Span.Services.FileOperations.MoveFileOperation(sourcePaths, destDir, router);
                if (hasConflicts)
                    moveOp.SetConflictResolution(resolution, applyToAll);
                op = moveOp;
            }
            else
            {
                var copyOp = new Span.Services.FileOperations.CopyFileOperation(sourcePaths, destDir, router);
                if (hasConflicts)
                    copyOp.SetConflictResolution(resolution, applyToAll);
                op = copyOp;
            }

            await ViewModel.ExecuteFileOperationAsync(op, activeIndex >= 0 ? activeIndex : null);

            if (isCut && _clipboardPaths.Count > 0) _clipboardPaths.Clear();
            UpdateToolbarButtonStates();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandlePaste error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ctrl+Shift+V: 클립보드 항목을 바로가기(.lnk)로 붙여넣기.
        /// WScript.Shell COM으로 .lnk 파일 생성.
        /// </summary>
        private async void HandlePasteAsShortcut()
        {
            try
            {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            string? destDir;
            if (viewMode != ViewMode.MillerColumns)
            {
                destDir = ViewModel.ActiveExplorer.CurrentFolder?.Path;
            }
            else
            {
                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                if (activeIndex < 0 || activeIndex >= columns.Count) return;
                destDir = columns[activeIndex].Path;
            }
            if (string.IsNullOrEmpty(destDir)) return;

            // 소스 경로 수집 (내부 or 외부 클립보드)
            List<string> sourcePaths;
            if (_clipboardPaths.Count > 0)
            {
                sourcePaths = new List<string>(_clipboardPaths);
            }
            else
            {
                try
                {
                    var content = Clipboard.GetContent();
                    if (!content.Contains(StandardDataFormats.StorageItems)) return;
                    var items = await content.GetStorageItemsAsync();
                    sourcePaths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    if (sourcePaths.Count == 0) return;
                }
                catch { return; }
            }

            int created = 0;
            foreach (var srcPath in sourcePaths)
            {
                try
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                    var lnkPath = System.IO.Path.Combine(destDir, $"{name} - Shortcut.lnk");

                    // 중복 방지
                    int suffix = 1;
                    while (System.IO.File.Exists(lnkPath))
                    {
                        lnkPath = System.IO.Path.Combine(destDir, $"{name} - Shortcut ({suffix}).lnk");
                        suffix++;
                    }

                    // WScript.Shell COM으로 .lnk 생성
                    var shellType = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"));
                    if (shellType == null) break;
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    var shortcut = shell.CreateShortcut(lnkPath);
                    shortcut.TargetPath = srcPath;
                    shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(srcPath) ?? "";
                    shortcut.Save();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                    created++;
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Shortcut] Failed to create shortcut for {srcPath}: {ex.Message}");
                }
            }

            if (created > 0)
            {
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_ShortcutsCreated"), created));
                HandleRefresh();
            }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandlePasteAsShortcut error: {ex.Message}");
            }
        }

        #endregion

        #region New Folder (Ctrl+Shift+N)

        // =================================================================
        //  P1: New Folder (Ctrl+Shift+N)
        // =================================================================

        /// <summary>
        /// 새 폴더 생성 처리. 현재 활성 컬럼 경로에 새 폴더를 만들고
        /// 인라인 이름 변경 모드를 시작한다.
        /// </summary>
        private async void HandleNewFolder()
        {
            try
            {
                var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                    ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

                FolderViewModel? currentFolder;
                int activeIndex;

                if (viewMode != ViewMode.MillerColumns)
                {
                    currentFolder = ViewModel.ActiveExplorer.CurrentFolder;
                    activeIndex = -1;
                }
                else
                {
                    var columns = ViewModel.ActiveExplorer.Columns;
                    activeIndex = GetCurrentColumnIndex(); // selection 기반 fallback 포함
                    if (activeIndex < 0) activeIndex = columns.Count - 1;
                    if (activeIndex < 0 || activeIndex >= columns.Count) return;
                    currentFolder = columns[activeIndex];
                }
                if (currentFolder == null) return;

                string baseName = _loc.Get("NewFolderBaseName");
                bool isRemote = Services.FileSystemRouter.IsRemotePath(currentFolder.Path);

                string newPath;
                if (isRemote)
                {
                    // 원격 경로: URI 호환 경로 조합 (Path.Combine 사용 불가)
                    newPath = currentFolder.Path.TrimEnd('/') + "/" + baseName;
                    // 원격 폴더 충돌 검사 스킵 — 서버에서 자동 처리
                }
                else
                {
                    newPath = System.IO.Path.Combine(currentFolder.Path, baseName);
                    int count = 1;
                    while (System.IO.Directory.Exists(newPath))
                    {
                        newPath = System.IO.Path.Combine(currentFolder.Path, $"{baseName} ({count})");
                        count++;
                    }
                }

                var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
                var op = new Span.Services.FileOperations.NewFolderOperation(newPath, router);
                await ViewModel.ExecuteFileOperationAsync(op, activeIndex >= 0 ? activeIndex : (int?)null);

                // Select the new folder and start inline rename
                var newFolder = currentFolder.Children.FirstOrDefault(c =>
                    c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                if (newFolder != null)
                {
                    currentFolder.SelectedChild = newFolder;
                    newFolder.BeginRename();
                    await System.Threading.Tasks.Task.Delay(100);
                    if (viewMode == ViewMode.MillerColumns && activeIndex >= 0)
                        FocusRenameTextBox(activeIndex);
                    // non-Miller: 해당 뷰에서 rename TextBox 포커스는 IsRenaming 바인딩으로 자동 처리
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandleNewFolder error: {ex.Message}");
            }
        }

        #endregion

        #region Refresh (F5)

        // =================================================================
        //  P1: Refresh (F5)
        // =================================================================

        /// <summary>
        /// 새로고침(Refresh) 처리. 현재 활성 컬럼을 다시 로드하쩰나
        /// Home 뷰에서는 드라이브 목록을 리로드한다.
        /// </summary>
        private async void HandleRefresh()
        {
            try
            {
                var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                    ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

                FolderViewModel? column;
                if (viewMode != ViewMode.MillerColumns)
                {
                    column = ViewModel.ActiveExplorer.CurrentFolder;
                }
                else
                {
                    var columns = ViewModel.ActiveExplorer.Columns;
                    int activeIndex = GetActiveColumnIndex();
                    if (activeIndex < 0) activeIndex = columns.Count - 1;
                    if (activeIndex < 0 || activeIndex >= columns.Count) return;
                    column = columns[activeIndex];
                }
                if (column == null) return;

                var previousSelection = column.SelectedChild;

                await column.ReloadAsync();

                // 이전 선택 복원 (이름 기준)
                if (previousSelection != null)
                {
                    var restored = column.Children.FirstOrDefault(c =>
                        c.Name.Equals(previousSelection.Name, StringComparison.OrdinalIgnoreCase));
                    if (restored != null)
                        column.SelectedChild = restored;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandleRefresh error: {ex.Message}");
            }
        }

        #endregion

        #region Rename (F2) - Inline Rename

        // =================================================================
        //  P2: Rename (F2) — 인라인 이름 변경
        // =================================================================

        /// <summary>
        /// 이름 변경 처리. 단일 선택 시 인라인 이름 변경,
        /// 다중 선택 시 배치 이름 변경 대화상자를 표시한다.
        /// </summary>
        private void HandleRename()
        {
            // Details/List/Icon 뷰: 해당 뷰의 자체 rename 핸들러에 위임
            if (ViewModel.CurrentViewMode == Models.ViewMode.Details)
            {
                GetActiveDetailsView()?.HandleRename();
                return;
            }
            if (ViewModel.CurrentViewMode == Models.ViewMode.List)
            {
                GetActiveListView()?.HandleRename();
                return;
            }
            if (Helpers.ViewModeExtensions.IsIconMode(ViewModel.CurrentViewMode))
            {
                GetActiveIconView()?.HandleRename();
                return;
            }

            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex(); // Fixed: Use GetCurrentColumnIndex
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

            // 다중 선택 → 배치 이름 변경 다이얼로그
            if (currentColumn.HasMultiSelection)
            {
                _ = ShowBatchRenameDialogAsync(currentColumn);
                return;
            }

            var selected = currentColumn.SelectedChild;

            // 선택된 항목이 없으면 첫 번째 항목 선택
            if (selected == null && currentColumn.Children.Count > 0)
            {
                selected = currentColumn.Children[0];
                currentColumn.SelectedChild = selected;
            }

            if (selected == null) return;

            // F2 cycling: if already renaming the same item, advance selection cycle
            var itemPath = (selected as FolderViewModel)?.Path ?? (selected as FileViewModel)?.Path;
            if (selected.IsRenaming && itemPath == _renameTargetPath)
            {
                // Cycle: 0(name) → 1(all) → 2(extension) → 0(name) ...
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    FocusRenameTextBox(activeIndex);
                });
                return;
            }

            // First F2 press: start rename with name-only selection
            _renameSelectionCycle = 0;
            _renameTargetPath = itemPath;
            selected.BeginRename();

            // TextBox에 포커스
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                FocusRenameTextBox(activeIndex);
            });
        }

        /// <summary>
        /// 다중 선택된 항목의 배치 이름 변경 다이얼로그 표시.
        /// </summary>
        private async System.Threading.Tasks.Task ShowBatchRenameDialogAsync(FolderViewModel currentColumn)
        {
            var items = currentColumn.GetSelectedItemsList();
            if (items.Count < 2) return;

            var dialog = new Views.Dialogs.BatchRenameDialog(items);
            dialog.XamlRoot = this.Content.XamlRoot;

            var result = await ShowContentDialogSafeAsync(dialog);
            if (result != ContentDialogResult.Primary) return;

            var renameList = dialog.GetRenameList();
            if (renameList.Count == 0) return;

            var op = new Services.FileOperations.BatchRenameOperation(renameList);
            await ViewModel.ExecuteFileOperationAsync(op);
        }

        /// <summary>
        /// 인라인 rename TextBox에 포커스를 맞추고 선택 영역 적용.
        /// Windows Explorer 방식 F2 cycling: 파일명만 → 전체 → 확장자만 → 파일명만 ...
        /// 폴더이거나 확장자가 없으면 항상 전체 선택.
        /// </summary>
        private void FocusRenameTextBox(int columnIndex)
        {
            var listView = GetListViewForColumn(columnIndex);
            if (listView == null)
            {
                // ListView를 아직 못 찾으면 한 번 더 지연 재시도
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    var retryList = GetListViewForColumn(columnIndex);
                    if (retryList != null) FocusRenameTextBoxCore(retryList, columnIndex);
                });
                return;
            }

            FocusRenameTextBoxCore(listView, columnIndex);
        }

        private void FocusRenameTextBoxCore(ListView listView, int columnIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            if (columnIndex >= columns.Count) { Helpers.DebugLogger.Log($"[Rename] FocusRenameTextBoxCore: columnIndex={columnIndex} >= columns.Count={columns.Count}"); return; }

            var column = columns[columnIndex];
            if (column.SelectedChild == null) { Helpers.DebugLogger.Log($"[Rename] FocusRenameTextBoxCore: SelectedChild is null for column {columnIndex}"); return; }

            int idx = column.Children.IndexOf(column.SelectedChild);
            Helpers.DebugLogger.Log($"[Rename] FocusRenameTextBoxCore: col={columnIndex} selectedChild='{column.SelectedChild.Name}' IsRenaming={column.SelectedChild.IsRenaming} childIdx={idx}");
            if (idx < 0) return;

            var container = listView.ContainerFromIndex(idx) as UIElement;
            if (container == null)
            {
                // 아이템이 가상화되어 아직 로드 안 된 경우 ScrollIntoView 후 재시도
                listView.ScrollIntoView(column.SelectedChild);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    var retryContainer = listView.ContainerFromIndex(idx) as UIElement;
                    if (retryContainer != null)
                    {
                        var tb = FindChild<TextBox>(retryContainer as DependencyObject);
                        if (tb != null) ApplyRenameSelection(tb, column.SelectedChild is FolderViewModel);
                    }
                });
                return;
            }

            var textBox = FindChild<TextBox>(container as DependencyObject);
            if (textBox != null)
            {
                ApplyRenameSelection(textBox, column.SelectedChild is FolderViewModel);
            }
        }

        /// <summary>
        /// TextBox에 포커스를 주고 F2 cycling에 따른 선택 영역을 적용.
        /// WinUI 3에서 Focus()가 선택 영역을 리셋하므로, Select()를 DispatcherQueue로 지연 실행.
        /// </summary>
        private void ApplyRenameSelection(TextBox textBox, bool isFolder)
        {
            textBox.Focus(FocusState.Keyboard);

            // Focus()가 선택 영역을 리셋하므로 DispatcherQueue로 지연 실행
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (_isClosed) return;
                if (!isFolder && !string.IsNullOrEmpty(textBox.Text))
                {
                    int dotIndex = textBox.Text.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        // F2 cycling: 0=name only, 1=all, 2=extension only
                        switch (_renameSelectionCycle)
                        {
                            case 0: // Name only (exclude extension)
                                textBox.Select(0, dotIndex);
                                break;
                            case 1: // All (including extension)
                                textBox.SelectAll();
                                break;
                            case 2: // Extension only
                                textBox.Select(dotIndex + 1, textBox.Text.Length - dotIndex - 1);
                                break;
                        }
                    }
                    else
                    {
                        textBox.SelectAll();
                    }
                }
                else
                {
                    textBox.SelectAll();
                }
            });
        }

        private void OnRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                vm.CommitRename();
                _justFinishedRename = true; // OnMillerKeyDown이 이 Enter를 파일 실행으로 처리하지 않도록
                _renameTargetPath = null; // Reset F2 cycle state
                e.Handled = true;
                FocusSelectedItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                vm.CancelRename();
                _justFinishedRename = true;
                _renameTargetPath = null; // Reset F2 cycle state
                e.Handled = true;
                FocusSelectedItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
            {
                // Up/Down 화살표가 ListView로 버블링되어 선택 변경 → 리네임 취소되는 것을 방지
                e.Handled = true;
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            Helpers.DebugLogger.Log($"[Rename] LostFocus: vm.Name='{vm.Name}' IsRenaming={vm.IsRenaming} pendingFocus={_renamePendingFocus}");

            // PerformRename(컨텍스트 메뉴) → BeginRename 직후 MenuFlyout 닫힘으로
            // TextBox가 아직 포커스를 받기 전에 LostFocus가 발동하는 케이스 방어
            if (_renamePendingFocus) return;

            // 포커스 잃으면 커밋 (ListModeView와 동일 동작)
            // Enter 없이 다른 곳 클릭해도 변경사항 저장
            if (vm.IsRenaming)
            {
                vm.CommitRename();
            }
            _justFinishedRename = true;
            _renameTargetPath = null; // Reset F2 cycle state
        }

        /// <summary>
        /// 현재 선택된 항목의 ListViewItem 컨테이너에 포커스를 복원.
        /// 이름 변경 후 화살표 키가 그 자리에서 동작하도록.
        /// </summary>
        private void FocusSelectedItem()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            if (column.SelectedChild == null) return;

            var listView = GetListViewForColumn(activeIndex);
            if (listView == null) return;

            int idx = column.Children.IndexOf(column.SelectedChild);
            if (idx < 0) return;

            // 약간의 딜레이 후 ListViewItem 컨테이너에 포커스
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                var container = listView.ContainerFromIndex(idx) as UIElement;
                container?.Focus(FocusState.Keyboard);
            });
        }

        /// <summary>
        /// 활성 상태인 인라인 이름 변경을 취소한다.
        /// </summary>
        private void CancelAnyActiveRename()
        {
            // 우클릭 메뉴에서 이름 바꾸기 실행 시, MenuFlyout 닫힘 → 컬럼 GotFocus → 여기 호출됨
            // _renamePendingFocus가 true이면 PerformRename이 진행 중이므로 취소하지 않음
            if (_renamePendingFocus) return;

            var explorer = ViewModel?.ActiveExplorer;
            if (explorer == null) return;

            // 최적화: _renameTargetPath가 있으면 해당 아이템만 찾아 취소 (14K 전수 순회 방지)
            if (_renameTargetPath != null)
            {
                foreach (var col in explorer.Columns)
                {
                    var child = col.SelectedChild;
                    if (child != null && child.IsRenaming)
                    {
                        child.CancelRename();
                        _justFinishedRename = true;
                        _renameTargetPath = null;
                        return;
                    }
                }
            }

            // Fallback: 경로 없으면 컬럼별 selectedChild만 확인
            bool cancelled = false;
            foreach (var col in explorer.Columns)
            {
                if (col.SelectedChild?.IsRenaming == true)
                {
                    col.SelectedChild.CancelRename();
                    cancelled = true;
                }
            }
            if (cancelled)
            {
                _justFinishedRename = true;
            }
            _renameTargetPath = null;
        }

        #endregion

        #region Delete Operations (Delete, Shift+Delete)

        // =================================================================
        //  P2: Delete (Delete key)
        // =================================================================

        /// <summary>
        /// 삭제 처리. 선택된 항목들을 휴지통으로 이동하거나 영구 삭제한다.
        /// 확인 대화상자를 표시하고 FileOperationManager를 통해 작업을 실행한다.
        /// </summary>
        private async void HandleDelete()
        {
            try
            {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            FolderViewModel? currentColumn;
            int activeIndex;

            if (viewMode != ViewMode.MillerColumns)
            {
                currentColumn = ViewModel.ActiveExplorer.CurrentFolder;
                activeIndex = -1;
            }
            else
            {
                // ★ Save activeIndex BEFORE showing dialog (modal dialog steals focus)
                var columns = ViewModel.ActiveExplorer.Columns;
                activeIndex = GetCurrentColumnIndex();
                if (activeIndex < 0 || activeIndex >= columns.Count) return;
                currentColumn = columns[activeIndex];
            }
            if (currentColumn == null) return;

            // Multi-selection support
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0 && currentColumn.Children.Count > 0)
            {
                currentColumn.SelectedChild = currentColumn.Children[0];
                selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
            }
            if (selectedItems.Count == 0) return;

            var selected = selectedItems[0]; // For display name in dialog
            // Remember the selected item's index for smart selection after delete
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            // Confirm delete (send to Recycle Bin)
            if (_settings.ConfirmDelete)
            {
                string confirmContent = selectedItems.Count == 1
                    ? string.Format(_loc.Get("DeleteConfirmContent"), selected.Name)
                    : string.Format(_loc.Get("DeleteConfirmContent"), $"{selectedItems.Count} items");

                var dialog = new ContentDialog
                {
                    Title = _loc.Get("DeleteConfirmTitle"),
                    Content = confirmContent,
                    PrimaryButtonText = _loc.Get("Delete"),
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await ShowContentDialogSafeAsync(dialog);
                if (result != ContentDialogResult.Primary) return;
                // await 후 상태 재검증 — dialog 표시 중 탭 전환/창 닫기 가능
                if (_isClosed) return;
            }

            // await 후 컬럼 유효성 재검증 (Miller only)
            if (viewMode == ViewMode.MillerColumns)
            {
                var freshColumns = ViewModel.ActiveExplorer.Columns;
                if (activeIndex >= freshColumns.Count) return;
                if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;
            }

            var paths = selectedItems.Select(i => i.Path).ToList();
            Helpers.DebugLogger.Log($"[HandleDelete] Dialog confirmed. Deleting {paths.Count} item(s), ActiveIndex: {activeIndex}");

            // Execute delete operation (send to Recycle Bin)
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: false, router: router);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex >= 0 ? activeIndex : null);
            if (_isClosed) return;

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Clamp(selectedIndex, 0, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
            }

            // Miller only: Remove columns after deleted item
            if (viewMode == ViewMode.MillerColumns && activeIndex >= 0)
            {
                ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);
                FocusColumnAsync(activeIndex);
            }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandleDelete error: {ex.Message}");
            }
        }

        /// <summary>
        /// 영구 삭제(Shift+Delete) 처리. 휴지통을 거치지 않고 영구 삭제한다.
        /// </summary>
        private async void HandlePermanentDelete()
        {
            try
            {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            FolderViewModel? currentColumn;
            int activeIndex;

            if (viewMode != ViewMode.MillerColumns)
            {
                currentColumn = ViewModel.ActiveExplorer.CurrentFolder;
                activeIndex = -1;
            }
            else
            {
                var columns = ViewModel.ActiveExplorer.Columns;
                activeIndex = GetCurrentColumnIndex(); // selection 기반 fallback 포함
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                if (activeIndex < 0 || activeIndex >= columns.Count) return;
                currentColumn = columns[activeIndex];
            }
            if (currentColumn == null) return;

            // Multi-selection support
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0) return;

            var selected = selectedItems[0];
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            string confirmContent = selectedItems.Count == 1
                ? string.Format(_loc.Get("PermanentDeleteContent"), selected.Name)
                : string.Format(_loc.Get("PermanentDeleteContent"), $"{selectedItems.Count} items");

            var dialog = new ContentDialog
            {
                Title = _loc.Get("PermanentDeleteTitle"),
                Content = confirmContent,
                PrimaryButtonText = _loc.Get("PermanentDelete"),
                CloseButtonText = _loc.Get("Cancel"),
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await ShowContentDialogSafeAsync(dialog);
            if (result != ContentDialogResult.Primary) return;

            // await 후 상태 재검증
            if (_isClosed) return;
            if (viewMode == ViewMode.MillerColumns)
            {
                var freshColumns = ViewModel.ActiveExplorer.Columns;
                if (activeIndex >= freshColumns.Count) return;
                if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;
            }

            // Execute permanent delete operation
            var paths = selectedItems.Select(i => i.Path).ToList();
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: true, router: router);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex >= 0 ? activeIndex : null);
            if (_isClosed) return;

            // ★ Smart selection
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Clamp(selectedIndex, 0, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
            }

            // Miller only: Remove columns after deleted item
            if (viewMode == ViewMode.MillerColumns && activeIndex >= 0)
            {
                ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);
                FocusColumnAsync(activeIndex);
            }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileOp] HandlePermanentDelete error: {ex.Message}");
            }
        }

        #endregion

        #region Search Box

        // =================================================================
        //  Search Box
        // =================================================================

        private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                // 재귀 검색 중이면 취소+복원
                var explorer = ViewModel.ActiveExplorer;
                if (explorer?.HasActiveSearchResults == true)
                {
                    explorer.CancelRecursiveSearch();
                    ViewModel.UpdateStatusBar();
                }
                // 기존 인라인 필터 복원
                else if (_isSearchFiltered)
                {
                    RestoreSearchFilter();
                }
                SearchBox.Text = string.Empty;
                GetActiveMillerColumnsControl().Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string queryText = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(queryText)) return;

                // Parse the query using Advanced Query Syntax
                var query = Helpers.SearchQueryParser.Parse(queryText);
                if (query.IsEmpty) return;

                var explorer = ViewModel.ActiveExplorer;
                if (explorer == null) return;

                // 기존 인라인 필터 복원 (재귀 검색 전)
                if (_isSearchFiltered)
                {
                    RestoreSearchFilter();
                }

                // 검색 루트 결정: 첫 번째 컬럼 = 네비게이션 루트 (macOS Finder 방식)
                // Miller Columns에서 D:\ → Projects → src 로 진입해도
                // Columns[0].Path = "D:\" 이므로 드라이브 전체 검색 가능.
                var rootFolder = explorer.Columns.FirstOrDefault();
                string rootPath = rootFolder?.Path ?? explorer.CurrentPath;
                if (string.IsNullOrEmpty(rootPath) || rootPath == "PC") return;

                // 숨김 파일 설정 확인
                bool showHidden = false;
                try
                {
                    var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                    if (settings != null) showHidden = settings.ShowHiddenFiles;
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[FileOp] Settings access error: {ex.Message}"); }

                // 재귀 검색 시작
                _ = explorer.StartRecursiveSearchAsync(query, rootPath, showHidden);

                e.Handled = true;
            }
        }

        // ── Search Filter State ──
        private bool _isSearchFiltered = false;
        private List<FileSystemViewModel>? _searchOriginalChildren = null;
        private int _searchFilteredColumnIndex = -1;

        /// <summary>
        /// Apply advanced search filter: replace column children with filtered results.
        /// Stores original children for restoration on Escape.
        /// </summary>
        private void ApplySearchFilter(FolderViewModel column, SearchQuery query, int columnIndex)
        {
            // Save original children if not already saved (allow re-filtering)
            var source = _isSearchFiltered && _searchOriginalChildren != null
                ? _searchOriginalChildren
                : column.Children.ToList();

            if (!_isSearchFiltered)
            {
                _searchOriginalChildren = column.Children.ToList();
                _searchFilteredColumnIndex = columnIndex;
            }

            var filtered = Helpers.SearchFilter.Apply(query, source);

            column.Children.Clear();
            foreach (var item in filtered)
                column.Children.Add(item);

            _isSearchFiltered = true;

            // Update status bar with search result count
            ViewModel.StatusItemCountText = $"Search: {filtered.Count} result{(filtered.Count != 1 ? "s" : "")}";
            if (filtered.Count == 0)
            {
                ViewModel.StatusSelectionText = "Esc to clear";
            }
        }

        /// <summary>
        /// Restore original column children after search filter is cleared.
        /// </summary>
        private void RestoreSearchFilter()
        {
            if (!_isSearchFiltered || _searchOriginalChildren == null) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (_searchFilteredColumnIndex >= 0 && _searchFilteredColumnIndex < columns.Count)
            {
                var column = columns[_searchFilteredColumnIndex];
                column.Children.Clear();
                foreach (var item in _searchOriginalChildren)
                    column.Children.Add(item);
            }

            _isSearchFiltered = false;
            _searchOriginalChildren = null;
            _searchFilteredColumnIndex = -1;
        }

        #endregion

        #region Toolbar Click Handlers

        private void OnCutClick(object sender, RoutedEventArgs e)
        {
            HandleCut();
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            HandleCopy();
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            HandlePaste();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            HandleDelete();
        }

        private void OnNewFolderClick(object sender, RoutedEventArgs e)
        {
            HandleNewFolder();
        }

        private void OnNewItemDropdownClick(object sender, RoutedEventArgs e)
        {
            var folderPath = GetActiveColumnPath();
            if (string.IsNullOrEmpty(folderPath)) return;

            var menu = _contextMenuService.BuildNewItemMenu(folderPath, this);
            menu.ShowAt(sender as FrameworkElement, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft
            });
        }

        private string? GetActiveColumnPath()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode != ViewMode.MillerColumns)
                return ViewModel.ActiveExplorer.CurrentFolder?.Path;

            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex(); // selection 기반 fallback (toolbar 클릭 시 focus 없음)
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return null;
            return columns[activeIndex].Path;
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            HandleRename();
        }

        #endregion

        #region Sort Operations

        // Sort handlers
        private void OnSortByName(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Name";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortByDate(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Date";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortBySize(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Size";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortByType(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Type";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortAscending(object sender, RoutedEventArgs e)
        {
            _currentSortAscending = true;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortDescending(object sender, RoutedEventArgs e)
        {
            _currentSortAscending = false;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        // ── Group By toolbar handlers ──

        private void OnGroupByNone(object sender, RoutedEventArgs e)
            => ((Services.IContextMenuHost)this).ApplyGroupBy("None");

        private void OnGroupByName(object sender, RoutedEventArgs e)
            => ((Services.IContextMenuHost)this).ApplyGroupBy("Name");

        private void OnGroupByType(object sender, RoutedEventArgs e)
            => ((Services.IContextMenuHost)this).ApplyGroupBy("Type");

        private void OnGroupByDate(object sender, RoutedEventArgs e)
            => ((Services.IContextMenuHost)this).ApplyGroupBy("DateModified");

        private void OnGroupBySize(object sender, RoutedEventArgs e)
            => ((Services.IContextMenuHost)this).ApplyGroupBy("Size");

        /// <summary>
        /// 정렬 필드명 매핑: UI("Date") → FolderViewModel("DateModified").
        /// </summary>
        private static string MapSortField(string uiField) => uiField switch
        {
            "Date" => "DateModified",
            _ => uiField
        };

        private void SortCurrentColumn(string sortBy, bool? ascending = null)
        {
            bool isAscending = ascending ?? _currentSortAscending;

            // FolderViewModel.SortChildren에 위임 (전체 뷰 모드 공통 정렬)
            var column = GetActiveSortColumn();
            if (column == null || column.Children.Count == 0) return;

            var mappedField = MapSortField(sortBy);
            column.SortChildren(mappedField, isAscending);

            // Icon/List 뷰 새로고침 (Miller 외 뷰에서는 별도 리빌드 필요)
            if (ViewModel.CurrentViewMode != ViewMode.MillerColumns)
            {
                GetActiveListView()?.RebuildListItemsPublic();
            }

            UpdateSortButtonIcons();
            Helpers.DebugLogger.Log($"[SortCurrentColumn] Sorted by {mappedField} ({(isAscending ? "Ascending" : "Descending")})");
        }

        /// <summary>
        /// 현재 활성 뷰 모드에 맞는 정렬 대상 FolderViewModel 반환.
        /// </summary>
        private FolderViewModel? GetActiveSortColumn()
        {
            if (ViewModel.CurrentViewMode == ViewMode.MillerColumns)
            {
                var activeIndex = GetCurrentColumnIndex();
                if (activeIndex < 0 || activeIndex >= ViewModel.ActiveExplorer.Columns.Count)
                    return null;
                return ViewModel.ActiveExplorer.Columns[activeIndex];
            }
            // Icon/List/Details: 현재 폴더
            return ViewModel.ActiveExplorer.CurrentFolder;
        }

        #endregion

        #region Duplicate and Properties

        private async void HandleDuplicateFile()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var sel = GetCurrentSelected();
                if (sel != null) selectedItems = new List<FileSystemViewModel> { sel };
            }
            if (selectedItems.Count == 0) return;

            var suffix = _loc.Get("DuplicateSuffix"); // " - Copy" / " - 복사본" / " - コピー"
            var paths = selectedItems.Select(item => item.Path).ToList();

            foreach (var srcPath in paths)
            {
                try
                {
                    bool isDir = System.IO.Directory.Exists(srcPath);
                    string dir = System.IO.Path.GetDirectoryName(srcPath) ?? "";
                    string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                    string ext = System.IO.Path.GetExtension(srcPath);

                    // Generate unique name: "file - Copy.txt", "file - Copy (2).txt", ...
                    string destPath;
                    if (isDir)
                    {
                        destPath = System.IO.Path.Combine(dir, nameWithoutExt + suffix);
                        int counter = 2;
                        while (System.IO.Directory.Exists(destPath))
                        {
                            destPath = System.IO.Path.Combine(dir, $"{nameWithoutExt}{suffix} ({counter})");
                            counter++;
                        }
                        await System.Threading.Tasks.Task.Run(() => CopyDirectoryRecursive(NormalizeLongPath(srcPath), NormalizeLongPath(destPath)));
                    }
                    else
                    {
                        destPath = System.IO.Path.Combine(dir, nameWithoutExt + suffix + ext);
                        int counter = 2;
                        while (System.IO.File.Exists(destPath))
                        {
                            destPath = System.IO.Path.Combine(dir, $"{nameWithoutExt}{suffix} ({counter}){ext}");
                            counter++;
                        }
                        await System.Threading.Tasks.Task.Run(() => System.IO.File.Copy(NormalizeLongPath(srcPath), NormalizeLongPath(destPath)));
                    }

                    Helpers.DebugLogger.Log($"[Duplicate] {srcPath} → {destPath}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Duplicate] Error: {ex.Message}");
                }
            }

            // Refresh current folder
            var refreshFolder = GetCurrentViewFolder();
            if (refreshFolder != null)
            {
                await refreshFolder.RefreshAsync();
            }

            ViewModel.ShowToast(paths.Count == 1
                ? string.Format(_loc.Get("Toast_Duplicated"), System.IO.Path.GetFileName(paths[0]))
                : string.Format(_loc.Get("Toast_DuplicatedMultiple"), paths.Count));
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            try
            {
                System.IO.Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[CopyDirectoryRecursive] CreateDirectory failed '{destDir}': {ex.Message}");
                return; // Cannot proceed without destination directory
            }
            foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            {
                try
                {
                    System.IO.File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)));
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[CopyDirectoryRecursive] Failed: '{file}': {ex.Message}");
                }
            }
            foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
            {
                try
                {
                    CopyDirectoryRecursive(dir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir)));
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[CopyDirectoryRecursive] Failed dir: '{dir}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Normalize path for long path support. .NET 8 handles long paths natively,
        /// but this ensures the \\?\ prefix is applied for paths exceeding MAX_PATH (260).
        /// </summary>
        private static string NormalizeLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            // Already has long path prefix or is a UNC path with prefix
            if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")) return path;
            // Only apply prefix for paths that exceed MAX_PATH
            if (path.Length >= 260)
            {
                if (path.StartsWith(@"\\"))
                    return @"\\?\UNC\" + path.Substring(2); // UNC path
                return @"\\?\" + path;
            }
            return path;
        }

        // =================================================================
        //  P1 #18: Alt+Enter — Show Windows Properties dialog
        // =================================================================

        private void HandleShowProperties()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var sel = GetCurrentSelected();
                if (sel != null) selectedItems = new List<FileSystemViewModel> { sel };
            }

            var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();

            if (selectedItems.Count > 0)
            {
                // Show properties for first selected item
                shellService.ShowProperties(selectedItems[0].Path);
            }
            else
            {
                // No selection: show properties for current folder
                var folderPath = ViewModel.ActiveExplorer?.CurrentFolder?.Path;
                if (!string.IsNullOrEmpty(folderPath))
                    shellService.ShowProperties(folderPath);
            }
        }

        #endregion
    }
}
