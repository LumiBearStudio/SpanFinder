using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Span.Helpers;
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
        // 드래그 시각 피드백용 브러시 캐시 (매 이벤트 할당 방지)
        private static readonly SolidColorBrush _dragHighlightBrush = new(Microsoft.UI.Colors.White) { Opacity = 0.08 };
        private static readonly SolidColorBrush _transparentBrush = new(Microsoft.UI.Colors.Transparent);
        private static readonly SolidColorBrush _sidebarHoverBrush = new(Microsoft.UI.Colors.White) { Opacity = 0.05 };
        private static readonly SolidColorBrush _gripHighlightBrush = new(Microsoft.UI.Colors.Gray) { Opacity = 0.3 };

        /// <summary>
        /// 내부 드래그 진행 중 플래그. 드래그 중 ActivePane 전환을 방지하기 위해 사용.
        /// </summary>
        internal bool IsDragInProgress { get; private set; }

        // Modifier key 폴링: 드래그 중 Ctrl/Shift/Alt 변경 시 DragOver 강제 재발생.
        // DispatcherTimer는 OLE 드래그 모달 루프 중 Tick이 발생하지 않으므로
        // System.Threading.Timer(스레드 풀 기반)를 사용한다.
        private System.Threading.Timer? _modifierPollTimer;
        private int _lastModifierSnapshot;
        private int _nudgeCountdown;

        // 커스텀 드래그 오버레이: WinUI 기본 ListView 행 스크린샷 + 시스템 Caption 대신
        // 앱 폰트/테마에 맞는 경량 오버레이로 아이콘+파일명+작업 텍스트를 표시한다.
        private Border? _dragOverlay;
        private TextBlock? _dragOverlayItemText;
        private TextBlock? _dragOverlayOpText;
        private Border? _dragOverlayBadge;
        private TextBlock? _dragOverlayBadgeText;
        private TranslateTransform? _dragOverlayTransform;
        // 아이콘 스택 레이어 (최대 3개, 반투명 겹침)
        private readonly TextBlock[] _dragOverlayIcons = new TextBlock[3];
        // 드래그 시작 시 캡처한 항목 정보 (DragOver에서 사용)
        private int _dragItemCount;
        private string _dragItemName = "";
        private List<string> _dragItemIcons = new();

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

            IsDragInProgress = true;
            StartModifierPollTimer();

            // 드래그 오버레이용 항목 정보 캡처 (최대 3개 아이콘 — 스택 표시용)
            _dragItemCount = items.Count;
            _dragItemName = items[0].Name;
            _dragItemIcons = items.Take(3).Select(i => i.IconGlyph).ToList();

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = DeterminePane(sender);
            // 모든 작업 유형(Copy/Move/Link)을 허용.
            // AcceptedOperation이 RequestedOperation의 부분집합이어야 WinUI가 수용하므로,
            // Shift=Move, Alt=Link, 기본(같은 드라이브)=Move가 동작하려면 모두 포함해야 한다.
            // 참고: SPAN→외부앱(Explorer) 드롭 시 Explorer가 자체 규칙으로 Move/Copy 결정.
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;

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
        /// 드래그 작업 완료(드롭 또는 취소) 시 IsDragInProgress 플래그를 해제한다.
        /// </summary>
        private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            IsDragInProgress = false;
            StopModifierPollTimer();
            HideDragTooltip();
            _dragItemCount = 0;
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
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[DragDrop] StorageItem resolve failed ({p}): {ex.Message}");
                    }
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
                e.DragUIOverride.IsCaptionVisible = false;
                e.DragUIOverride.IsGlyphVisible = false;
                UpdateDragTooltip(_loc.Get("DragAddToFavorites"), e, sender as UIElement ?? (UIElement)Content);
            }
        }

        /// <summary>
        /// 즐겨찾기 사이드바에 드롭 시 드롭된 경로를 즐겨찾기 목록에 추가한다.
        /// </summary>
        private async void OnFavoritesDrop(object sender, DragEventArgs e)
        {
            HideDragTooltip();
            try
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
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] OnFavoritesDrop error: {ex.Message}");
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
                    e.DragUIOverride.IsCaptionVisible = false;
                    e.DragUIOverride.IsGlyphVisible = false;
                    HideDragTooltip();
                    e.Handled = true;
                    return;
                }
                // Prevent dropping parent into child
                if (srcPaths.Any(p => targetFolder.Path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.DragUIOverride.IsCaptionVisible = false;
                    e.DragUIOverride.IsGlyphVisible = false;
                    HideDragTooltip();
                    e.Handled = true;
                    return;
                }
            }

            var mode = ResolveDragDropMode(e, targetFolder.Path);

            e.AcceptedOperation = ToAcceptedOperation(mode);
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            UpdateDragTooltip(GetDragCaption(mode, targetFolder.Name), e, sender as UIElement ?? (UIElement)Content);

            // Visual feedback: highlight background (캐시된 브러시 사용)
            grid.Background = _dragHighlightBrush;

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
            HideDragTooltip();

            try
            {
                // Reset highlight and cancel spring-load
                grid.Background = _transparentBrush;
                StopSpringLoadTimer();

                var paths = await ExtractDropPaths(e);
                if (paths.Count == 0) return;

                var mode = ResolveDragDropMode(e, targetFolder.Path);
                await HandleDropAsync(paths, targetFolder.Path, mode);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] OnFolderItemDrop error: {ex.Message}");
            }
        }

        /// <summary>
        /// 폴더 아이템에서 드래그 나갈 시 스프링 로디드 타이머를 정지하고 시각적 피드백을 초기화한다.
        /// </summary>
        private void OnFolderItemDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = _transparentBrush;
            }

            // Cancel spring-loaded timer when leaving the target folder
            if (sender is Grid g && g.DataContext is FolderViewModel leavingFolder
                && leavingFolder == _springLoadTarget)
            {
                StopSpringLoadTimer();
            }

            HideDragTooltip();
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

            // Same-folder check: block Move only when source and target are in the SAME pane.
            // Cross-pane drags (Split View) should always be allowed even if the column shows
            // the same folder path, because the user explicitly intends to move between panes.
            bool isSameFolder = false;
            bool isCrossPane = false;
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                isSameFolder = srcPaths.All(p => System.IO.Path.GetDirectoryName(p)?.Equals(folderVm.Path, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (e.DataView.Properties.TryGetValue("SourcePane", out var spObj) && spObj is string srcPane)
            {
                var targetPane = IsDescendant(RightPaneContainer, sender as DependencyObject) ? "Right" : "Left";
                isCrossPane = srcPane != targetPane;
            }

            var mode = ResolveDragDropMode(e, folderVm.Path);

            // Same-folder Move → block (no-op). Copy/Link in same folder are allowed.
            if (isSameFolder && mode == DragDropMode.Move && !isCrossPane)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.DragUIOverride.IsCaptionVisible = false;
                e.DragUIOverride.IsGlyphVisible = false;
                HideDragTooltip();
                e.Handled = true;
                return;
            }

            e.AcceptedOperation = ToAcceptedOperation(mode);
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            UpdateDragTooltip(GetDragCaption(mode, folderVm.Name), e, sender as UIElement ?? (UIElement)Content);
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
            HideDragTooltip();

            try
            {
                var paths = await ExtractDropPaths(e);
                if (paths.Count == 0) return;

                var mode = ResolveDragDropMode(e, folderVm.Path);
                await HandleDropAsync(paths, folderVm.Path, mode);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] OnColumnDrop error: {ex.Message}");
            }
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
        /// 드래그 앤 드롭 작업 유형. Windows 탐색기와 동일한 수정키 규칙을 따른다.
        /// </summary>
        private enum DragDropMode { Move, Copy, Link }

        /// <summary>
        /// Resolves drag-drop operation based on modifier keys and drive comparison.
        /// Windows Explorer convention:
        ///   Shift = force Move, Ctrl = force Copy, Alt = create shortcut (Link).
        ///   Default: same drive = Move, different drive = Copy.
        /// </summary>
        private DragDropMode ResolveDragDropMode(DragEventArgs e, string destFolder)
        {
            // OLE 드래그 모달 루프 중 InputKeyboardSource는 stale 상태를 반환할 수 있으므로
            // GetAsyncKeyState (하드웨어 직접 읽기)를 사용한다.
            var shift = (Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_SHIFT) & 0x8000) != 0;
            var ctrl = (Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_CONTROL) & 0x8000) != 0;
            var alt = (Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_MENU) & 0x8000) != 0;

            // Explicit modifier keys override default behavior
            if (alt) return DragDropMode.Link;    // Alt = create shortcut
            if (shift) return DragDropMode.Move;   // Shift = force Move
            if (ctrl) return DragDropMode.Copy;    // Ctrl = force Copy

            // Default: same drive root = Move, different drive = Copy
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths && srcPaths.Count > 0)
            {
                var srcRoot = System.IO.Path.GetPathRoot(srcPaths[0]);
                var destRoot = System.IO.Path.GetPathRoot(destFolder);
                if (!string.IsNullOrEmpty(srcRoot) && !string.IsNullOrEmpty(destRoot)
                    && srcRoot.Equals(destRoot, StringComparison.OrdinalIgnoreCase))
                    return DragDropMode.Move;
            }

            return DragDropMode.Copy; // fallback: Copy
        }

        /// <summary>드래그 캡션 문자열 (Move/Copy/Link).</summary>
        private string GetDragCaption(DragDropMode mode, string folderName)
        {
            return mode switch
            {
                DragDropMode.Move => $"{_loc.Get("Move")} → {folderName}",
                DragDropMode.Link => $"{_loc.Get("DragLink") ?? "바로가기 만들기"} → {folderName}",
                _ => $"{_loc.Get("Copy")} → {folderName}",
            };
        }

        /// <summary>DragDropMode → WinUI DataPackageOperation 매핑.</summary>
        private static DataPackageOperation ToAcceptedOperation(DragDropMode mode) => mode switch
        {
            DragDropMode.Move => DataPackageOperation.Move,
            DragDropMode.Link => DataPackageOperation.Link,
            _ => DataPackageOperation.Copy,
        };

        /// <summary>
        /// 커스텀 드래그 오버레이를 초기화하여 루트 Grid에 추가한다.
        /// 항목의 실제 아이콘을 반투명 겹침(stack)으로 표시하여
        /// macOS Finder / Windows Explorer와 유사한 시각 피드백을 제공한다.
        ///
        /// 단일:  [📁]  FolderName       복수:  [📁📄📷] ③  3개 항목
        ///        복사 → TEST                    이동 → TEST
        /// </summary>
        private void EnsureDragOverlay()
        {
            if (_dragOverlay != null) return;

            var iconService = Services.IconService.Current;
            var iconFont = new FontFamily(iconService?.FontFamilyPath ?? "/Assets/Fonts/remixicon.ttf#remixicon");
            double fontSize = 12 + _iconFontScaleLevel;
            double largeIconSize = 26 + _iconFontScaleLevel;
            double badgeFontSize = Math.Max(9, 10 + _iconFontScaleLevel);

            _dragOverlayTransform = new TranslateTransform();

            // ── 아이콘 스택: 최대 3개 아이콘을 반투명 겹침으로 표시 ──
            // 레이어 0(뒤) → 1(중간) → 2(앞) 순으로 opacity/offset 증가
            double[][] offsets = { new[] { -5.0, -3.0 }, new[] { 0.0, 0.0 }, new[] { 5.0, 3.0 } };
            double[] opacities = { 0.35, 0.6, 1.0 };

            var iconStackGrid = new Grid();
            for (int i = 0; i < 3; i++)
            {
                _dragOverlayIcons[i] = new TextBlock
                {
                    FontFamily = iconFont,
                    FontSize = largeIconSize,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SpanTextPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = opacities[i],
                    RenderTransform = new TranslateTransform { X = offsets[i][0], Y = offsets[i][1] },
                    Visibility = Visibility.Collapsed,
                };
                iconStackGrid.Children.Add(_dragOverlayIcons[i]);
            }

            var iconPanel = new Border
            {
                Child = iconStackGrid,
                Background = (SolidColorBrush)Application.Current.Resources["SpanBgHoverBrush"],
                CornerRadius = new CornerRadius(6),
                Width = 44 + _iconFontScaleLevel * 2,
                Height = 40 + _iconFontScaleLevel * 2,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            // 카운트 뱃지: 아이콘 패널 우하단 오버레이
            _dragOverlayBadgeText = new TextBlock
            {
                FontSize = badgeFontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _dragOverlayBadge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SpanAccentBrush"],
                CornerRadius = new CornerRadius(8),
                MinWidth = 18,
                Height = 18,
                Padding = new Thickness(4, 0, 4, 0),
                Child = _dragOverlayBadgeText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                Visibility = Visibility.Collapsed,
            };

            var iconWithBadge = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { iconPanel, _dragOverlayBadge },
            };

            // ── 텍스트 영역 ──
            _dragOverlayItemText = new TextBlock
            {
                FontSize = fontSize,
                Foreground = (SolidColorBrush)Application.Current.Resources["SpanTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180,
            };
            _dragOverlayOpText = new TextBlock
            {
                FontSize = Math.Max(10, fontSize - 1),
                Foreground = (SolidColorBrush)Application.Current.Resources["SpanTextSecondaryBrush"],
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var textStack = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _dragOverlayItemText, _dragOverlayOpText },
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { iconWithBadge, textStack },
            };

            _dragOverlay = new Border
            {
                Child = content,
                Background = (SolidColorBrush)Application.Current.Resources["SpanBgLayer2Brush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["SpanBorderControlBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 14, 8),
                RenderTransform = _dragOverlayTransform,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.96,
            };
            Canvas.SetZIndex(_dragOverlay, 10000);

            if (Content is Grid rootGrid)
            {
                Grid.SetRowSpan(_dragOverlay, 10);
                Grid.SetColumnSpan(_dragOverlay, 10);
                rootGrid.Children.Add(_dragOverlay);
            }
        }

        /// <summary>
        /// 드래그 오버레이를 갱신한다.
        /// 내부 드래그: 항목의 실제 아이콘을 반투명 스택으로 표시 + 파일명/개수 + 뱃지.
        /// 외부 드래그(Explorer→SPAN): 작업 텍스트만 표시.
        /// </summary>
        private void UpdateDragTooltip(string opText, DragEventArgs e, UIElement relativeTo)
        {
            EnsureDragOverlay();

            bool isInternal = _dragItemCount > 0 && IsDragInProgress;

            if (isInternal)
            {
                // 아이콘 스택 구성: 캡처된 아이콘(최대 3개)을 레이어별 할당.
                // 단일 → 레이어2만 (중앙, 풀 opacity)
                // 2개  → 레이어1 + 2
                // 3+개 → 레이어0 + 1 + 2
                int iconCount = _dragItemIcons.Count;
                int startLayer = 3 - iconCount; // 0, 1, or 2

                for (int i = 0; i < 3; i++)
                {
                    if (i >= startLayer && (i - startLayer) < iconCount)
                    {
                        _dragOverlayIcons[i].Text = _dragItemIcons[i - startLayer];
                        _dragOverlayIcons[i].Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _dragOverlayIcons[i].Visibility = Visibility.Collapsed;
                    }
                }

                if (_dragItemCount == 1)
                {
                    _dragOverlayItemText!.Text = _dragItemName;
                    _dragOverlayBadge!.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _dragOverlayItemText!.Text = string.Format(_loc.Get("FolderItemCount"), _dragItemCount);
                    _dragOverlayBadge!.Visibility = Visibility.Visible;
                    _dragOverlayBadgeText!.Text = _dragItemCount.ToString();
                }

                e.DragUIOverride.IsContentVisible = false;
            }
            else
            {
                // 외부 드래그: 아이콘 숨김
                for (int i = 0; i < 3; i++)
                    _dragOverlayIcons[i].Visibility = Visibility.Collapsed;
                _dragOverlayItemText!.Text = "";
                _dragOverlayBadge!.Visibility = Visibility.Collapsed;
            }

            _dragOverlayOpText!.Text = opText;

            var pos = e.GetPosition(Content as UIElement);
            _dragOverlayTransform!.X = pos.X + 16;
            _dragOverlayTransform!.Y = pos.Y + 20;
            _dragOverlay!.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 드래그 오버레이를 숨긴다. DragLeave/Drop/AcceptedOperation=None 시 호출.
        /// </summary>
        private void HideDragTooltip()
        {
            if (_dragOverlay != null)
                _dragOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 드래그 중 modifier key(Ctrl/Shift/Alt) 변경을 감지하여 DragOver를 강제 재발생시키는 타이머.
        /// WinUI 3에서 DragOver는 마우스 이동 시에만 발생하고,
        /// OLE 드래그 모달 루프 중에는 DispatcherTimer.Tick이 발생하지 않으므로
        /// System.Threading.Timer(스레드 풀)를 사용하여 독립적으로 폴링한다.
        /// modifier key 변경 감지 시 mouse_event로 1px 미세 이동을 주입하여 DragOver를 트리거.
        /// </summary>
        private void StartModifierPollTimer()
        {
            _lastModifierSnapshot = GetModifierSnapshot();
            _modifierPollTimer = new System.Threading.Timer(
                OnModifierPollTick, null, 50, 50);
        }

        private void StopModifierPollTimer()
        {
            _modifierPollTimer?.Dispose();
            _modifierPollTimer = null;
        }

        private void OnModifierPollTick(object? state)
        {
            if (!IsDragInProgress) return;

            var snapshot = GetModifierSnapshot();
            if (snapshot != _lastModifierSnapshot)
            {
                _lastModifierSnapshot = snapshot;
                _nudgeCountdown = 3; // 변경 감지 → 3회 연속 nudge로 DragOver 확실히 트리거
            }

            if (_nudgeCountdown > 0)
            {
                _nudgeCountdown--;
                // SetCursorPos를 동일 좌표로 호출 → 커서 이동 없이 WM_MOUSEMOVE 발생.
                // Windows는 좌표 변경 여부와 무관하게 항상 WM_MOUSEMOVE를 생성하므로
                // OLE 드래그 모달 루프에서도 DragOver가 재발생한다.
                Helpers.NativeMethods.GetCursorPos(out var pt);
                Helpers.NativeMethods.SetCursorPos(pt.X, pt.Y);
            }
        }

        /// <summary>
        /// Win32 GetAsyncKeyState로 현재 modifier key 상태를 비트마스크로 반환.
        /// 스레드 무관하게 동작하므로 OLE 드래그 모달 루프 중에도 사용 가능.
        /// </summary>
        private static int GetModifierSnapshot()
        {
            int flags = 0;
            if ((Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_SHIFT) & 0x8000) != 0) flags |= 1;
            if ((Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_CONTROL) & 0x8000) != 0) flags |= 2;
            if ((Helpers.NativeMethods.GetAsyncKeyState(Helpers.NativeMethods.VK_MENU) & 0x8000) != 0) flags |= 4;
            return flags;
        }

        /// <summary>
        /// 드롭 작업을 실제로 실행한다.
        /// 충돌 처리 대화상자 표시, 파일 작업 실행, 대상 컬럼 리로드를 처리한다.
        /// </summary>
        private async System.Threading.Tasks.Task HandleDropAsync(List<string> sourcePaths, string destFolder, DragDropMode mode)
        {
            // Validate: don't drop onto itself or into child
            sourcePaths = sourcePaths.Where(p =>
                !p.Equals(destFolder, StringComparison.OrdinalIgnoreCase) &&
                !destFolder.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Safety net: filter out same-folder Move (items already in destFolder)
            if (mode == DragDropMode.Move)
            {
                sourcePaths = sourcePaths.Where(p =>
                    !string.Equals(System.IO.Path.GetDirectoryName(p), destFolder, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            if (sourcePaths.Count == 0) return;

            // Link mode: create .lnk shortcut files
            if (mode == DragDropMode.Link)
            {
                await CreateShortcutsAsync(sourcePaths, destFolder);
                return;
            }

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

                    var dialogResult = await ShowContentDialogSafeAsync(dialog);
                    if (dialogResult != ContentDialogResult.Primary)
                    {
                        Helpers.DebugLogger.Log("[DragDrop] Drop cancelled by user (conflict dialog)");
                        return;
                    }

                    resolution = vm.SelectedResolution;
                    Helpers.DebugLogger.Log($"[DragDrop] Conflict resolution: {resolution}");
                }
            }

            bool isMove = mode == DragDropMode.Move;
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

            // Find which column corresponds to destFolder for targeted refresh.
            // Search ActiveExplorer first, then opposite explorer (split view cross-pane drop).
            // ExecuteFileOperationAsync already calls RefreshOppositeExplorerAsync for split view,
            // so we only need to find targetColumnIndex for the active explorer's refresh.
            int? targetColumnIndex = null;
            ExplorerViewModel? targetExplorer = null;

            if (ViewModel?.ActiveExplorer?.Columns != null)
            {
                for (int i = 0; i < ViewModel.ActiveExplorer.Columns.Count; i++)
                {
                    if (ViewModel.ActiveExplorer.Columns[i].Path.Equals(destFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        targetColumnIndex = i;
                        targetExplorer = ViewModel.ActiveExplorer;
                        break;
                    }
                }
            }

            // If not found in ActiveExplorer, check opposite explorer (cross-pane drop)
            if (targetColumnIndex == null && ViewModel.IsSplitViewEnabled)
            {
                var opposite = ViewModel.ActivePane == Models.ActivePane.Left
                    ? ViewModel.RightExplorer : ViewModel.LeftExplorer;
                if (opposite?.Columns != null)
                {
                    for (int i = 0; i < opposite.Columns.Count; i++)
                    {
                        if (opposite.Columns[i].Path.Equals(destFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            targetColumnIndex = i;
                            targetExplorer = opposite;
                            break;
                        }
                    }
                }
            }

            // Execute file operation — RefreshOppositeExplorerAsync handles split view refresh
            await ViewModel.ExecuteFileOperationAsync(op, targetColumnIndex);

            // If target was in the opposite explorer, also refresh that specific column
            if (targetExplorer != null && targetExplorer != ViewModel.ActiveExplorer)
            {
                await ViewModel.RefreshCurrentFolderAsync(targetColumnIndex, targetExplorer);
            }

            // Move 완료 후 소스 폴더가 포함된 비활성 탭도 리프레시.
            // 활성 탭과 Split View 반대 패널은 이미 위에서 처리되었으므로,
            // 여기서는 다른 탭의 Explorer만 대상으로 한다.
            if (isMove)
            {
                var sourceFolders = sourcePaths
                    .Select(p => System.IO.Path.GetDirectoryName(p))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

                var activeExplorer = ViewModel.ActiveExplorer;
                foreach (var tab in ViewModel.Tabs)
                {
                    var explorer = tab.Explorer;
                    if (explorer == null || explorer == activeExplorer) continue;
                    // Split View 반대 패널도 이미 처리됨
                    if (ViewModel.IsSplitViewEnabled && explorer == (ViewModel.ActivePane == Models.ActivePane.Left
                        ? ViewModel.RightExplorer : ViewModel.LeftExplorer)) continue;

                    for (int i = 0; i < explorer.Columns.Count; i++)
                    {
                        if (sourceFolders.Contains(explorer.Columns[i].Path))
                        {
                            await ViewModel.RefreshCurrentFolderAsync(i, explorer);
                            break;
                        }
                    }
                }
            }

            Helpers.DebugLogger.Log($"[DragDrop] {(isMove ? "Moved" : "Copied")} {sourcePaths.Count} item(s) to {destFolder}");
        }

        /// <summary>
        /// Alt+드래그: 대상 폴더에 .lnk 바로가기 파일을 생성한다.
        /// Windows Shell IShellLink COM 인터페이스를 사용하여 표준 .lnk 파일 생성.
        /// </summary>
        private async System.Threading.Tasks.Task CreateShortcutsAsync(List<string> sourcePaths, string destFolder)
        {
            int created = 0;
            var errors = new List<string>();

            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var srcPath in sourcePaths)
                {
                    try
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                        var lnkPath = System.IO.Path.Combine(destFolder, name + ".lnk");

                        // Avoid overwriting existing shortcuts
                        if (File.Exists(lnkPath))
                        {
                            int suffix = 1;
                            do
                            {
                                lnkPath = System.IO.Path.Combine(destFolder, $"{name} ({suffix}).lnk");
                                suffix++;
                            } while (File.Exists(lnkPath));
                        }

                        Helpers.ShortcutHelper.CreateShortcut(lnkPath, srcPath);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{System.IO.Path.GetFileName(srcPath)}: {ex.Message}");
                        Helpers.DebugLogger.Log($"[DragDrop] Shortcut creation failed: {ex.Message}");
                    }
                }
            });

            // Refresh target folder
            int? targetColumnIndex = null;
            if (ViewModel?.ActiveExplorer?.Columns != null)
            {
                for (int i = 0; i < ViewModel.ActiveExplorer.Columns.Count; i++)
                {
                    if (ViewModel.ActiveExplorer.Columns[i].Path.Equals(destFolder, StringComparison.OrdinalIgnoreCase))
                    { targetColumnIndex = i; break; }
                }
            }
            await ViewModel.RefreshCurrentFolderAsync(targetColumnIndex);
            await ViewModel.RefreshCurrentFolderAsync(0,
                ViewModel.ActivePane == Models.ActivePane.Left ? ViewModel.RightExplorer : ViewModel.LeftExplorer);

            if (errors.Count > 0)
                ViewModel.ShowError(string.Join("\n", errors));
            else
                ViewModel.ShowToast(string.Format(_loc.Get("Toast_Completed"),
                    $"{_loc.Get("DragLink") ?? "바로가기"} {created}"));

            Helpers.DebugLogger.Log($"[DragDrop] Created {created} shortcut(s) in {destFolder}");
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
            var mode = ResolveDragDropMode(e, destFolder);

            // Same-folder Move → block (items already in destFolder). Copy/Link allowed.
            // Only applies to internal drags — external drops always allowed.
            bool isSameFolder = false;
            if (isInternalDrag && sourcePane == targetPane
                && e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj2) && srcObj2 is List<string> srcPaths2)
            {
                isSameFolder = srcPaths2.All(p =>
                    System.IO.Path.GetDirectoryName(p)?.Equals(destFolder, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (isSameFolder && mode == DragDropMode.Move)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.DragUIOverride.IsCaptionVisible = false;
                e.DragUIOverride.IsGlyphVisible = false;
                HideDragTooltip();
                e.Handled = true;
                return;
            }

            e.AcceptedOperation = ToAcceptedOperation(mode);
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            UpdateDragTooltip(GetDragCaption(mode, targetExplorer?.CurrentFolder?.Name ?? ""), e, sender as UIElement ?? (UIElement)Content);

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
            HideDragTooltip();

            try
            {
                // External drags (Windows Explorer etc.) won't have "SourcePane" property
                bool isInternalDrag = e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s;
                var sourcePane = isInternalDrag ? (string)sp! : "";

                bool isLeftTarget = fe.Name == "LeftPaneContainer";
                string targetPane = isLeftTarget ? "Left" : "Right";

                var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
                var destFolder = targetExplorer?.CurrentFolder?.Path ?? "";
                if (string.IsNullOrEmpty(destFolder)) return;

                var mode = ResolveDragDropMode(e, destFolder);

                // Same-folder Move is blocked — only for internal drags
                if (isInternalDrag && sourcePane == targetPane && mode == DragDropMode.Move)
                {
                    var paths2 = await ExtractDropPaths(e);
                    bool isSameFolder = paths2.All(p =>
                        System.IO.Path.GetDirectoryName(p)?.Equals(destFolder, StringComparison.OrdinalIgnoreCase) == true);
                    if (isSameFolder) return;
                }

                // Hide overlay
                var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
                overlay.Opacity = 0;

                var paths = await ExtractDropPaths(e);
                if (paths.Count == 0) return;

                await HandleDropAsync(paths, destFolder, mode);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] OnPaneDrop error: {ex.Message}");
            }
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
            HideDragTooltip();
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
                grid.Background = _sidebarHoverBrush;
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
                grid.Background = _transparentBrush;
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
                rect.Fill = _gripHighlightBrush;
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
                rect.Fill = _transparentBrush;
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
                        var grid = VisualTreeHelpers.FindChild<Grid>(container);
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
                    rect.Fill = _transparentBrush;
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
                var grid = VisualTreeHelpers.FindChild<Grid>(container);
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
        /// 단일 TextBlock을 재사용하여 14K 아이템 폴더에서 대량 할당 방지.
        /// </summary>
        [ThreadStatic]
        private static TextBlock? _measureTextBlock;

        private static double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var tb = _measureTextBlock ??= new TextBlock { TextWrapping = TextWrapping.NoWrap };
            tb.Text = text;
            tb.FontSize = fontSize;
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
                var grid = VisualTreeHelpers.FindChild<Grid>(container);
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
                var grid = VisualTreeHelpers.FindChild<Grid>(container);
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
