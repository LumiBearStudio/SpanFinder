using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.ViewModels;
using Span.Views.Dialogs;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// MainWindow의 드래그 앤 드롭 처리 부분 클래스.
    /// Miller Column 내 파일/폴더 드래그, 즐겨찾기 드롭, 폴더 간 드롭,
    /// 외부 애플리케이션 간 StorageItems 교환, 스프링 로디드 폴더,
    /// 컬럼 리사이즈 그립 등의 기능을 담당한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        #region Drag & Drop: Drag start and Favorites

        /// <summary>
        /// Miller Column ListView에서 드래그 시작 시 호출.
        /// 드래그 데이터에 경로 목록과 출처 패널 정보를 설정하고,
        /// 외부 앱 드롭을 위한 StorageItems를 지연 로딩으로 제공한다.
        /// 러버밴드 선택 중에는 드래그를 취소한다.
        /// </summary>
        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Cancel file D&D if rubber-band selection is active
            if (_rubberBandHelpers.Values.Any(h => h.IsActive))
            { e.Cancel = true; return; }

            // Allow dragging both files and folders
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0) { e.Cancel = true; return; }

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = DeterminePane(sender);
            // Default to Copy for external drop targets (Windows Explorer, Desktop).
            // WinUI→Shell bridge defaults to Move when Move flag is present, ignoring
            // cross-drive convention. Internal Span drops use HandleDropAsync directly,
            // so they are unaffected by RequestedOperation.
            e.Data.RequestedOperation = DataPackageOperation.Copy;

            // Span→외부 앱: StorageItems를 지연 로딩 (외부 앱이 요청할 때만 로드)
            // DragItemsStarting에서 await 사용 금지 — async void + await는 드래그 종료 시
            // UI 스레드 데드락 유발 (DataPackage freeze 후 async 연속이 수정 시도)
            var capturedPaths = new List<string>(paths);
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });
        }

        /// <summary>
        /// 이벤트 발신자(ListView)가 좌측/우측 탐색기 중 어느 패널에 속하는지 판단한다.
        /// </summary>
        private string DeterminePane(object sender)
        {
            if (sender is DependencyObject depObj)
            {
                if (IsDescendant(RightPaneContainer, depObj))
                    return "Right";
            }
            return "Left";
        }

        /// <summary>
        /// Deferred StorageItems provider for drag-and-drop to external apps.
        /// Called lazily only when an external app (e.g. Windows Explorer) requests the data.
        /// </summary>
        private static async System.Threading.Tasks.Task ProvideStorageItemsAsync(
            Windows.ApplicationModel.DataTransfer.DataProviderRequest request,
            List<string> paths,
            Windows.ApplicationModel.DataTransfer.DataProviderDeferral deferral)
        {
            try
            {
                var storageItems = new List<Windows.Storage.IStorageItem>();
                foreach (var p in paths)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(p));
                        else if (System.IO.File.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p));
                    }
                    catch { }
                }
                request.SetData(storageItems);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] StorageItems provider error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// 즐겨찾기 사이드바 영역에 드래그 오버 시 AcceptedOperation을 설정한다.
        /// 폴더/파일 드롭을 Link 작업으로 표시하여 즐겨찾기 추가 의도를 나타낸다.
        /// </summary>
        private void OnFavoritesDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text) ||
                e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Link;
                e.DragUIOverride.Caption = _loc.Get("DragAddToFavorites");
            }
        }

        /// <summary>
        /// 즐겨찾기 사이드바에 드롭 시 드롭된 경로를 즐겨찾기 목록에 추가한다.
        /// </summary>
        private async void OnFavoritesDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var path = await e.DataView.GetTextAsync();
                if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                {
                    ViewModel.AddToFavorites(path);
                    Helpers.DebugLogger.Log($"[Sidebar] Folder dropped to favorites: {path}");
                }
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Path) && System.IO.Directory.Exists(item.Path))
                    {
                        ViewModel.AddToFavorites(item.Path);
                        Helpers.DebugLogger.Log($"[Sidebar] External folder dropped to favorites: {item.Path}");
                    }
                }
            }
        }

        #endregion

        #region Drag & Drop: Folder item targets (drop file onto a folder)

        /// <summary>
        /// 폴더 아이템 위에 드래그 오버 시 AcceptedOperation을 설정하고
        /// 스프링 로디드 타이머를 시작한다.
        /// 자기 자신에 드롭, 소스와 대상 동일 등의 무효 드롭을 방지한다.
        /// </summary>
        private void OnFolderItemDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;

            // Check if data contains paths (internal or external app)
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths") &&
                !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            // Prevent dropping onto self (check source paths)
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                if (srcPaths.Any(p => p.Equals(targetFolder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
                // Prevent dropping parent into child
                if (srcPaths.Any(p => targetFolder.Path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
            }

            bool isMove = ResolveDragDropOperation(e, targetFolder.Path);

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove
                ? $"{_loc.Get("Move")} → {targetFolder.Name}"
                : $"{_loc.Get("Copy")} → {targetFolder.Name}";
            e.DragUIOverride.IsCaptionVisible = true;

            // Visual feedback: highlight background
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.08 };

            // Spring-loaded folder: start timer if hovering over a new folder
            if (_springLoadTarget != targetFolder)
            {
                StopSpringLoadTimer();
                _springLoadTarget = targetFolder;
                _springLoadGrid = grid;
                StartSpringLoadTimer();
            }

            e.Handled = true;
        }

        /// <summary>
        /// 폴더 아이템에 드롭 시 파일 작업(복사/이동)을 실행한다.
        /// 스프링 로디드 타이머를 정지하고 드롭 대상 폴더로 파일 작업을 실행한다.
        /// </summary>
        private async void OnFolderItemDrop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;
            e.Handled = true; // Prevent bubbling BEFORE await (avoid duplicate execution)

            // Reset highlight and cancel spring-load
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            StopSpringLoadTimer();

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            bool isMove = ResolveDragDropOperation(e, targetFolder.Path);
            await HandleDropAsync(paths, targetFolder.Path, isMove: isMove);
        }

        /// <summary>
        /// 폴더 아이템에서 드래그 나갈 시 스프링 로디드 타이머를 정지하고 시각적 피드백을 초기화한다.
        /// </summary>
        private void OnFolderItemDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            // Cancel spring-loaded timer when leaving the target folder
            if (sender is Grid g && g.DataContext is FolderViewModel leavingFolder
                && leavingFolder == _springLoadTarget)
            {
                StopSpringLoadTimer();
            }
        }

        #endregion

        #region Spring-loaded folders: auto-open folder after drag hover delay

        /// <summary>
        /// 스프링 로디드 타이머를 시작하여 지정된 폴더 위에서 일정 시간 호버 시 자동 열림을 준비한다.
        /// </summary>
        private void StartSpringLoadTimer()
        {
            _springLoadTimer = new DispatcherTimer();
            _springLoadTimer.Interval = TimeSpan.FromMilliseconds(SPRING_LOAD_DELAY_MS);
            _springLoadTimer.Tick += OnSpringLoadTimerTick;
            _springLoadTimer.Start();
        }

        /// <summary>
        /// 스프링 로디드 타이머를 정지하고 관련 상태를 초기화한다.
        /// </summary>
        private void StopSpringLoadTimer()
        {
            if (_springLoadTimer != null)
            {
                _springLoadTimer.Stop();
                _springLoadTimer.Tick -= OnSpringLoadTimerTick;
                _springLoadTimer = null;
            }
            _springLoadTarget = null;
            _springLoadGrid = null;
        }

        /// <summary>
        /// 스프링 로디드 타이머 틱 이벤트.
        /// 드래그 호버 중인 폴더를 자동으로 열어 하위 폴더를 표시한다.
        /// </summary>
        private void OnSpringLoadTimerTick(object? sender, object e)
        {
            var folder = _springLoadTarget;
            StopSpringLoadTimer(); // One-shot: stop and clear state

            if (folder == null) return;

            // Navigate into the folder by selecting it in its parent column
            var explorer = ViewModel.ActiveExplorer;
            if (explorer != null)
            {
                foreach (var col in explorer.Columns)
                {
                    if (col.Children.Contains(folder))
                    {
                        col.SelectedChild = folder;
                        break;
                    }
                }
                Helpers.DebugLogger.Log($"[SpringLoad] Auto-opened folder: {folder.Name}");
            }
        }

        #endregion

        #region Drag & Drop: Column-level targets (drop into current folder)

        /// <summary>
        /// Miller Column 빈 영역에 드래그 오버 시 AcceptedOperation을 설정한다.
        /// 수정키(Shift/Ctrl)에 따라 이동/복사를 결정한다.
        /// </summary>
        private void OnColumnDragOver(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths") &&
                !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            // Same-folder check: block Move, allow Copy (Ctrl)
            bool isSameFolder = false;
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                isSameFolder = srcPaths.All(p => System.IO.Path.GetDirectoryName(p)?.Equals(folderVm.Path, StringComparison.OrdinalIgnoreCase) == true);
            }

            bool isMove = ResolveDragDropOperation(e, folderVm.Path);

            if (isSameFolder && isMove)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove
                ? $"{_loc.Get("Move")} → {folderVm.Name}"
                : $"{_loc.Get("Copy")} → {folderVm.Name}";
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true; // Prevent bubbling to PaneDragOver
        }

        /// <summary>
        /// Miller Column 빈 영역에 드롭 시 파일 작업(복사/이동)을 실행한다.
        /// 대상 경로는 해당 컬럼의 FolderViewModel 경로이다.
        /// </summary>
        private async void OnColumnDrop(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;
            e.Handled = true; // Prevent bubbling to OnPaneDrop (duplicate execution)

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            bool isMove = ResolveDragDropOperation(e, folderVm.Path);
            await HandleDropAsync(paths, folderVm.Path, isMove: isMove);
        }

        #endregion

        #region Drag & Drop: Shared helpers

        /// <summary>
        /// 드롭 이벤트에서 파일 경로 목록을 추출한다.
        /// 내부 Span 드래그(SourcePaths)와 외부 앱 StorageItems를 모두 지원한다.
        /// </summary>
        private async Task<List<string>> ExtractDropPaths(DragEventArgs e)
        {
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
                return srcPaths;

            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                    return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            // 외부 앱(Windows 탐색기 등)에서 드래그된 StorageItems 처리
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                return items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            }

            return new List<string>();
        }

        /// <summary>
        /// Resolves drag-drop operation based on modifier keys and drive comparison.
        /// Windows Explorer convention: same drive = Move, different drive = Copy.
        /// Shift forces Move, Ctrl forces Copy.
        /// </summary>
        private bool ResolveDragDropOperation(DragEventArgs e, string destFolder)
        {
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Explicit modifier keys override default behavior
            if (shift) return true;   // Shift = force Move
            if (ctrl) return false;   // Ctrl = force Copy

            // Default: same drive root = Move, different drive = Copy
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths && srcPaths.Count > 0)
            {
                var srcRoot = System.IO.Path.GetPathRoot(srcPaths[0]);
                var destRoot = System.IO.Path.GetPathRoot(destFolder);
                if (!string.IsNullOrEmpty(srcRoot) && !string.IsNullOrEmpty(destRoot))
                    return srcRoot.Equals(destRoot, StringComparison.OrdinalIgnoreCase);
            }

            return false; // fallback: Copy
        }

        /// <summary>
        /// 드롭 작업을 실제로 실행한다.
        /// 충돌 처리 대화상자 표시, 파일 작업 실행, 대상 컬럼 리로드를 처리한다.
        /// </summary>
        private async System.Threading.Tasks.Task HandleDropAsync(List<string> sourcePaths, string destFolder, bool isMove)
        {
            // Validate: don't drop onto itself or into child
            sourcePaths = sourcePaths.Where(p =>
                !p.Equals(destFolder, StringComparison.OrdinalIgnoreCase) &&
                !destFolder.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Safety net: filter out same-folder Move (items already in destFolder)
            if (isMove)
            {
                sourcePaths = sourcePaths.Where(p =>
                    !string.Equals(System.IO.Path.GetDirectoryName(p), destFolder, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            if (sourcePaths.Count == 0) return;

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();

            // Pre-check for conflicts (local destinations only)
            bool destIsRemote = FileSystemRouter.IsRemotePath(destFolder);
            ConflictResolution resolution = ConflictResolution.KeepBoth;
            bool hasConflicts = false;

            if (!destIsRemote)
            {
                string? firstConflictSrc = null;
                string? firstConflictDest = null;

                foreach (var srcPath in sourcePaths)
                {
                    var fileName = System.IO.Path.GetFileName(srcPath);
                    var destPath = System.IO.Path.Combine(destFolder, fileName);
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
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
                        Helpers.DebugLogger.Log($"[DragDrop] Conflict info error: {ex.Message}");
                    }

                    var dialog = new FileConflictDialog(vm);
                    dialog.XamlRoot = this.Content.XamlRoot;

                    var dialogResult = await dialog.ShowAsync();
                    if (dialogResult != ContentDialogResult.Primary)
                    {
                        Helpers.DebugLogger.Log("[DragDrop] Drop cancelled by user (conflict dialog)");
                        return;
                    }

                    resolution = vm.SelectedResolution;
                    Helpers.DebugLogger.Log($"[DragDrop] Conflict resolution: {resolution}");
                }
            }

            IFileOperation op;
            if (isMove)
            {
                var moveOp = new MoveFileOperation(sourcePaths, destFolder, router);
                if (hasConflicts)
                    moveOp.SetConflictResolution(resolution, true);
                op = moveOp;
            }
            else
            {
                var copyOp = new CopyFileOperation(sourcePaths, destFolder, router);
                if (hasConflicts)
                    copyOp.SetConflictResolution(resolution, true);
                op = copyOp;
            }

            // Find which column corresponds to destFolder for targeted refresh
            int? targetColumnIndex = null;
            if (ViewModel?.ActiveExplorer?.Columns != null)
            {
                for (int i = 0; i < ViewModel.ActiveExplorer.Columns.Count; i++)
                {
                    if (ViewModel.ActiveExplorer.Columns[i].Path.Equals(destFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        targetColumnIndex = i;
                        break;
                    }
                }
            }

            await ViewModel.ExecuteFileOperationAsync(op, targetColumnIndex);

            Helpers.DebugLogger.Log($"[DragDrop] {(isMove ? "Moved" : "Copied")} {sourcePaths.Count} item(s) to {destFolder}");
        }

        #endregion

        #region Drag & Drop: Cross-pane (left <-> right)

        /// <summary>
        /// 좌측/우측 패널 영역에 드래그 오버 시 AcceptedOperation을 설정한다.
        /// 크로스패널 드롭 시 대상 패널의 현재 경로로 드롭 작업을 설정한다.
        /// </summary>
        private void OnPaneDragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            // Determine source and target panes
            // External drags (Windows Explorer etc.) won't have "SourcePane" property
            bool isInternalDrag = e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s;
            var sourcePane = isInternalDrag ? (string)sp! : "";

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
            var destFolder = targetExplorer?.CurrentFolder?.Path ?? "";
            bool isMove = ResolveDragDropOperation(e, destFolder);

            // Same-pane drag: block Move (no-op), allow Copy (Ctrl)
            // Only applies to internal drags — external drops always allowed
            if (isInternalDrag && sourcePane == targetPane)
            {
                if (isMove)
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
                // Same-pane Copy → allow (fall through to set operation below)
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove ? _loc.Get("Move") : _loc.Get("Copy");
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;

            // Show drop overlay
            var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
            overlay.Opacity = 0.05;

            e.Handled = true;
        }

        /// <summary>
        /// 좌측/우측 패널 영역에 드롭 시 파일 작업(복사/이동)을 실행한다.
        /// </summary>
        private async void OnPaneDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            // External drags (Windows Explorer etc.) won't have "SourcePane" property
            bool isInternalDrag = e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s;
            var sourcePane = isInternalDrag ? (string)sp! : "";

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            // Same-pane Move is blocked (only Copy allowed) — only for internal drags
            bool isMove = false;
            {
                var targetExplorer2 = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
                var destFolder2 = targetExplorer2?.CurrentFolder?.Path ?? "";
                isMove = ResolveDragDropOperation(e, destFolder2);
            }
            if (isInternalDrag && sourcePane == targetPane && isMove) return;

            // Hide overlay
            var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
            overlay.Opacity = 0;

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            // Destination = target pane's current folder
            var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
            var destFolder = targetExplorer?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(destFolder)) return;

            // isMove already resolved above (same-pane Move was early-returned)
            await HandleDropAsync(paths, destFolder, isMove: isMove);
            e.Handled = true;
        }

        /// <summary>
        /// 좌측/우측 패널 영역에서 드래그 나갈 시 시각적 피드백을 초기화한다.
        /// </summary>
        private void OnPaneDragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                bool isLeftTarget = fe.Name == "LeftPaneContainer";
                var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
                overlay.Opacity = 0;
            }
        }

        #endregion

        #region Sidebar item hover effects

        /// <summary>
        /// Sidebar item hover effect - show subtle background.
        /// </summary>
        private void OnSidebarItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.White)
                { Opacity = 0.05 };
                Helpers.CursorHelper.SetHandCursor(grid);
            }
        }

        /// <summary>
        /// Sidebar item hover exit - remove background.
        /// </summary>
        private void OnSidebarItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Transparent);
            }
        }

        #endregion

        #region Column Resize Grip Handlers (Miller Columns drag-to-resize)

        /// <summary>
        /// 컬럼 리사이즈 그립에 마우스 진입 시 수평 리사이즈 커서를 표시한다.
        /// </summary>
        private void OnColumnResizeGripPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.3 };
                // Set resize cursor via InputSystemCursor (reliable in WinUI 3)
                SetGripCursor(rect, true);
            }
        }

        /// <summary>
        /// 컬럼 리사이즈 그립에서 마우스 나갈 시 기본 커서로 복원한다.
        /// </summary>
        private void OnColumnResizeGripPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizingColumn && sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                SetGripCursor(rect, false);
            }
        }

        private void OnColumnResizeGripPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                // Walk up to find the parent Grid that has the Width
                var parentGrid = VisualTreeHelper.GetParent(rect) as Grid;
                if (parentGrid == null) return;

                _isResizingColumn = true;
                _resizingColumnGrid = parentGrid;
                _resizeStartX = e.GetCurrentPoint(null).Position.X;
                _resizeStartWidth = parentGrid.Width;

                rect.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnColumnResizeGripPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingColumn && _resizingColumnGrid != null)
            {
                double currentX = e.GetCurrentPoint(null).Position.X;
                double delta = currentX - _resizeStartX;
                double newWidth = Math.Max(150, _resizeStartWidth + delta);
                newWidth = Math.Min(600, newWidth); // max width cap
                _resizingColumnGrid.Width = newWidth;

                // Ctrl+drag: apply the same width to ALL columns simultaneously
                var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                           .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (ctrl)
                {
                    var control = GetActiveMillerColumnsControl();
                    var columns = ViewModel.ActiveExplorer.Columns;
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var container = control.ContainerFromIndex(i) as ContentPresenter;
                        if (container == null) continue;
                        var grid = FindChild<Grid>(container);
                        if (grid != null && grid != _resizingColumnGrid)
                        {
                            grid.Width = newWidth;
                        }
                    }
                }

                // Force parent StackPanel and ScrollViewer to recalculate scroll extent
                if (VisualTreeHelper.GetParent(_resizingColumnGrid) is FrameworkElement parent)
                    parent.InvalidateMeasure();

                e.Handled = true;
            }
        }

        private void OnColumnResizeGripPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingColumn)
            {
                var grid = _resizingColumnGrid;
                _isResizingColumn = false;
                _resizingColumnGrid = null;

                if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
                {
                    rect.ReleasePointerCapture(e.Pointer);
                    rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    SetGripCursor(rect, false);
                }

                // Final layout pass: invalidate ItemsControl → StackPanel → ScrollViewer
                if (grid != null)
                {
                    var control = GetActiveMillerColumnsControl();
                    control.InvalidateMeasure();
                    control.UpdateLayout();
                    var scrollViewer = GetActiveMillerScrollViewer();
                    scrollViewer.InvalidateMeasure();
                    scrollViewer.UpdateLayout();
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Double-click on column resize grip: auto-fit column width to its content.
        /// Measures the widest item name in the column and resizes to fit.
        /// </summary>
        private void OnColumnResizeGripDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Microsoft.UI.Xaml.Shapes.Rectangle rect) return;

            var parentGrid = VisualTreeHelper.GetParent(rect) as Grid;
            if (parentGrid == null) return;

            // Find the column index by locating this grid in the ItemsControl
            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;
            int columnIndex = -1;

            for (int i = 0; i < columns.Count; i++)
            {
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid == parentGrid)
                {
                    columnIndex = i;
                    break;
                }
            }

            if (columnIndex < 0 || columnIndex >= columns.Count) return;

            double fittedWidth = MeasureColumnContentWidth(columns[columnIndex]);
            parentGrid.Width = fittedWidth;

            // Check if Ctrl is held: apply to all columns
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl)
            {
                ApplyWidthToAllColumns(fittedWidth);
            }

            // Invalidate layout
            control.InvalidateMeasure();
            control.UpdateLayout();
            var scrollViewer = GetActiveMillerScrollViewer();
            scrollViewer.InvalidateMeasure();
            scrollViewer.UpdateLayout();

            e.Handled = true;
        }

        /// <summary>
        /// Measure the ideal width for a column based on its content.
        /// Estimates text width from item display names plus icon/padding/chevron.
        /// Returns clamped width between 120 and 600 pixels.
        /// </summary>
        private double MeasureColumnContentWidth(FolderViewModel column)
        {
            const double iconWidth = 16;
            const double iconMargin = 12;
            const double itemPadding = 12 * 2;   // left + right padding on item grid
            const double chevronWidth = 14;       // chevron icon + opacity area
            const double countBadgeExtra = 30;    // child count text badge
            const double gripWidth = 4;           // resize grip
            const double scrollBarBuffer = 8;     // scrollbar safety margin
            const double minWidth = 120;
            const double maxWidth = 600;

            double maxItemWidth = 0;

            foreach (var child in column.Children)
            {
                string displayName = child.DisplayName;
                // Measure text using a TextBlock for accurate font metrics
                double textWidth = MeasureTextWidth(displayName, 14); // default font size 14

                double itemWidth = itemPadding + iconWidth + iconMargin + textWidth;

                // Folders have count badge + chevron
                if (child is FolderViewModel folderChild)
                {
                    itemWidth += countBadgeExtra + chevronWidth;
                }

                if (itemWidth > maxItemWidth)
                    maxItemWidth = itemWidth;
            }

            // Add grip width and buffer
            double totalWidth = maxItemWidth + gripWidth + scrollBarBuffer;

            return Math.Clamp(totalWidth, minWidth, maxWidth);
        }

        /// <summary>
        /// Measure the pixel width of a string using WinUI text rendering.
        /// </summary>
        private static double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextWrapping = TextWrapping.NoWrap
            };
            tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize.Width;
        }

        /// <summary>
        /// Apply a given width to all column grids in the active Miller Columns control.
        /// Used by Ctrl+drag and Ctrl+Shift+= shortcut.
        /// </summary>
        private void ApplyWidthToAllColumns(double width)
        {
            width = Math.Clamp(width, 150, 600);

            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;

            for (int i = 0; i < columns.Count; i++)
            {
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid != null)
                {
                    grid.Width = width;
                }
            }

            // Invalidate layout
            if (VisualTreeHelper.GetParent(control) is FrameworkElement parent)
                parent.InvalidateMeasure();
        }

        /// <summary>
        /// Auto-fit all column widths to their individual content.
        /// Each column gets its own optimal width based on the widest item it contains.
        /// </summary>
        private void AutoFitAllColumns()
        {
            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;

            for (int i = 0; i < columns.Count; i++)
            {
                double fittedWidth = MeasureColumnContentWidth(columns[i]);
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid != null)
                {
                    grid.Width = fittedWidth;
                }
            }

            // Invalidate layout
            control.InvalidateMeasure();
            control.UpdateLayout();
            var scrollViewer = GetActiveMillerScrollViewer();
            scrollViewer.InvalidateMeasure();
            scrollViewer.UpdateLayout();
        }

        /// <summary>
        /// Set cursor on resize grip element using WinUI 3 ProtectedCursor (via reflection).
        /// This is more reliable than Win32 SetCursor which gets overridden by WinUI message loop.
        /// </summary>
        private static void SetGripCursor(UIElement element, bool resize)
        {
            try
            {
                var cursor = resize
                    ? Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast)
                    : Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                // ProtectedCursor is protected; use reflection to bypass
                typeof(UIElement).GetProperty("ProtectedCursor",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(element, cursor);
            }
            catch
            {
                // Fallback: ignore on older platforms
            }
        }

        #endregion
    }
}
