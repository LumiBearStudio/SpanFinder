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
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    public sealed partial class MainWindow
    {
        #region Selection Operations (SelectAll, SelectNone, InvertSelection)

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
            // Details/Icon views: Extended mode natively handles Ctrl+A via ListView/GridView
        }

        // =================================================================
        //  Select None (Ctrl+Shift+A)
        // =================================================================

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
        }

        // =================================================================
        //  Invert Selection (Ctrl+I)
        // =================================================================

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

                ViewModel.UpdateStatusBar();
            }
        }

        // =================================================================
        //  Helper: Get current selected items (multi or single)
        // =================================================================

        private List<FileSystemViewModel> GetCurrentSelectedItems()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return new List<FileSystemViewModel>();

            var col = columns[activeIndex];
            return col.GetSelectedItemsList();
        }

        #endregion

        #region Clipboard Operations (Copy, Cut, Paste)

        private void HandleCopy()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                // Fallback: auto-select first item if nothing is selected
                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetCurrentColumnIndex();
                if (activeIndex >= 0 && activeIndex < columns.Count)
                {
                    var currentColumn = columns[activeIndex];
                    if (currentColumn.Children.Count > 0)
                    {
                        currentColumn.SelectedChild = currentColumn.Children[0];
                        selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
                    }
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

        private void HandleCut()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetCurrentColumnIndex();
                if (activeIndex >= 0 && activeIndex < columns.Count)
                {
                    var currentColumn = columns[activeIndex];
                    if (currentColumn.Children.Count > 0)
                    {
                        currentColumn.SelectedChild = currentColumn.Children[0];
                        selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
                    }
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

        private async void HandlePaste()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var targetFolder = columns[activeIndex];
            string destDir = targetFolder.Path;

            List<string> sourcePaths;
            bool isCut;

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

                    var items = await content.GetStorageItemsAsync();
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

                    var dialogResult = await dialog.ShowAsync();
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

            await ViewModel.ExecuteFileOperationAsync(op, activeIndex);

            if (isCut && _clipboardPaths.Count > 0) _clipboardPaths.Clear();
            UpdateToolbarButtonStates();
        }

        /// <summary>
        /// Ctrl+Shift+V: 클립보드 항목을 바로가기(.lnk)로 붙여넣기.
        /// WScript.Shell COM으로 .lnk 파일 생성.
        /// </summary>
        private async void HandlePasteAsShortcut()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            string destDir = columns[activeIndex].Path;

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

        private static void CopyDirectory(string src, string dest)
        {
            var dir = new System.IO.DirectoryInfo(src);
            System.IO.Directory.CreateDirectory(dest);
            foreach (var file in dir.GetFiles())
                file.CopyTo(System.IO.Path.Combine(dest, file.Name), true);
            foreach (var subDir in dir.GetDirectories())
                CopyDirectory(subDir.FullName, System.IO.Path.Combine(dest, subDir.Name));
        }

        #endregion

        #region New Folder (Ctrl+Shift+N)

        // =================================================================
        //  P1: New Folder (Ctrl+Shift+N)
        // =================================================================

        private async void HandleNewFolder()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentFolder = columns[activeIndex];
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
            await ViewModel.ExecuteFileOperationAsync(op, activeIndex);

            // Select the new folder and start inline rename
            var newFolder = currentFolder.Children.FirstOrDefault(c =>
                c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
            if (newFolder != null)
            {
                currentFolder.SelectedChild = newFolder;
                newFolder.BeginRename();
                await System.Threading.Tasks.Task.Delay(100);
                FocusRenameTextBox(activeIndex);
            }
        }

        #endregion

        #region Refresh (F5)

        // =================================================================
        //  P1: Refresh (F5)
        // =================================================================

        private async void HandleRefresh()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
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

        #endregion

        #region Rename (F2) - Inline Rename

        // =================================================================
        //  P2: Rename (F2) — 인라인 이름 변경
        // =================================================================

        private void HandleRename()
        {
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

            var result = await dialog.ShowAsync();
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
            if (columnIndex >= columns.Count) return;

            var column = columns[columnIndex];
            if (column.SelectedChild == null) return;

            int idx = column.Children.IndexOf(column.SelectedChild);
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
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            // 포커스 잃으면 취소 (ESC와 동일)
            // IsRenaming이 이미 false여도 정리 작업은 수행
            if (vm.IsRenaming)
            {
                vm.CancelRename();
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

        private void CancelAnyActiveRename()
        {
            var explorer = ViewModel?.ActiveExplorer;
            if (explorer == null) return;
            bool cancelled = false;
            foreach (var col in explorer.Columns)
            {
                foreach (var child in col.Children)
                {
                    if (child.IsRenaming)
                    {
                        child.CancelRename();
                        cancelled = true;
                    }
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

        private async void HandleDelete()
        {
            // ★ Save activeIndex BEFORE showing dialog (modal dialog steals focus)
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

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

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;
                // await 후 상태 재검증 — dialog 표시 중 탭 전환/창 닫기 가능
                if (_isClosed) return;
            }

            // await 후 컬럼 유효성 재검증
            var freshColumns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex >= freshColumns.Count) return;
            if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;

            var paths = selectedItems.Select(i => i.Path).ToList();
            Helpers.DebugLogger.Log($"[HandleDelete] Dialog confirmed. Deleting {paths.Count} item(s), ActiveIndex: {activeIndex}");
            Helpers.DebugLogger.Log($"[HandleDelete] Columns before delete: {string.Join(" > ", ViewModel.ActiveExplorer.Columns.Select(c => c.Name))}");

            // Execute delete operation (send to Recycle Bin)
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: false, router: router);
            Helpers.DebugLogger.Log($"[HandleDelete] Calling ExecuteFileOperationAsync with targetColumnIndex={activeIndex}");
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);
            if (_isClosed) return;

            Helpers.DebugLogger.Log($"[HandleDelete] After ExecuteFileOperationAsync. CurrentColumn children count: {currentColumn.Children.Count}");

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            // Note: RefreshCurrentFolderAsync() already cleared selection and reloaded
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Clamp(selectedIndex, 0, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
                Helpers.DebugLogger.Log($"[HandleDelete] Smart selection: selectedIndex={selectedIndex}, newIndex={newIndex}, selected={currentColumn.Children[newIndex].Name}");
            }
            else
            {
                Helpers.DebugLogger.Log($"[HandleDelete] No children after delete - selection cleared");
            }

            // Remove columns after deleted item (using proper cleanup)
            Helpers.DebugLogger.Log($"[HandleDelete] Cleaning up columns from index {activeIndex + 1}");
            ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);

            Helpers.DebugLogger.Log($"[HandleDelete] Columns after cleanup: {string.Join(" > ", ViewModel.ActiveExplorer.Columns.Select(c => c.Name))}");

            // Restore focus
            Helpers.DebugLogger.Log($"[HandleDelete] Restoring focus to column {activeIndex}");
            FocusColumnAsync(activeIndex);
            Helpers.DebugLogger.Log($"[HandleDelete] ===== COMPLETE =====");
        }

        private async void HandlePermanentDelete()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

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

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // await 후 상태 재검증
            if (_isClosed) return;
            var freshColumns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex >= freshColumns.Count) return;
            if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;

            // Execute permanent delete operation
            var paths = selectedItems.Select(i => i.Path).ToList();
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: true, router: router);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);
            if (_isClosed) return;

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            // Note: RefreshCurrentFolderAsync() already cleared selection and reloaded
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Clamp(selectedIndex, 0, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
            }

            // Remove columns after deleted item (using proper cleanup)
            ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);

            // Restore focus
            FocusColumnAsync(activeIndex);
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
                // Clear search and restore original column contents if filtered
                if (_isSearchFiltered)
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

                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                if (activeIndex < 0 || activeIndex >= columns.Count) return;

                var column = columns[activeIndex];

                // Parse the query using Advanced Query Syntax
                var query = Helpers.SearchQueryParser.Parse(queryText);

                if (query.IsEmpty) return;

                // Check if query has advanced filters (kind:, size:, date:, ext:)
                bool hasAdvancedFilters = query.KindFilter.HasValue ||
                                          query.SizeFilter.HasValue ||
                                          query.DateFilter.HasValue ||
                                          !string.IsNullOrEmpty(query.ExtensionFilter);

                if (hasAdvancedFilters)
                {
                    // Advanced search: filter the column's children in-place
                    ApplySearchFilter(column, query, activeIndex);
                }
                else
                {
                    // Simple name search: find first match and select it (existing behavior)
                    var source = _isSearchFiltered && _searchOriginalChildren != null
                        ? _searchOriginalChildren
                        : column.Children.ToList();

                    var match = Helpers.SearchFilter.FindFirst(query, source);
                    if (match != null)
                    {
                        // If filtered, restore first so we can select the match
                        if (_isSearchFiltered)
                        {
                            RestoreSearchFilter();
                        }
                        column.SelectedChild = match;
                        var listView = GetListViewForColumn(activeIndex);
                        listView?.ScrollIntoView(match);
                    }
                }

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
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
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
                        await System.Threading.Tasks.Task.Run(() => CopyDirectoryRecursive(srcPath, destPath));
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
                        await System.Threading.Tasks.Task.Run(() => System.IO.File.Copy(srcPath, destPath));
                    }

                    Helpers.DebugLogger.Log($"[Duplicate] {srcPath} → {destPath}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Duplicate] Error: {ex.Message}");
                }
            }

            // Refresh current folder
            var explorer = ViewModel.ActiveExplorer;
            int colIndex = GetCurrentColumnIndex();
            if (colIndex >= 0 && colIndex < explorer.Columns.Count)
            {
                await explorer.Columns[colIndex].RefreshAsync();
            }

            ViewModel.ShowToast(paths.Count == 1
                ? string.Format(_loc.Get("Toast_Duplicated"), System.IO.Path.GetFileName(paths[0]))
                : string.Format(_loc.Get("Toast_DuplicatedMultiple"), paths.Count));
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            System.IO.Directory.CreateDirectory(destDir);
            foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            {
                System.IO.File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)));
            }
            foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir)));
            }
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
