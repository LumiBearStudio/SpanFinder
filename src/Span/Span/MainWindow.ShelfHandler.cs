using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// File Shelf(임시 수집함) 관련 이벤트 핸들러.
    /// </summary>
    public partial class MainWindow
    {
        private ShelfService? _shelfService;

        private ShelfService ShelfSvc
            => _shelfService ??= App.Current.Services.GetRequiredService<ShelfService>();

        // ── Initialization ──────────────────────────────────────

        private void InitializeShelf()
        {
            ViewModel.ShelfItems.CollectionChanged += OnShelfItemsCollectionChanged;
            UpdateShelfUI();
        }

        private void OnShelfItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateShelfUI();
        }

        private void UpdateShelfUI()
        {
            var count = ViewModel.ShelfItems.Count;
            var loc = _loc;

            ShelfCountText.Text = $"Shelf ({count})";

            var hasItems = count > 0;
            ShelfMoveHereButton.IsEnabled = hasItems;
            ShelfCopyHereButton.IsEnabled = hasItems;
            ShelfClearButton.IsEnabled = hasItems;

            ShelfMoveText.Text = loc.Get("ShelfMoveHere");
            ShelfCopyText.Text = loc.Get("ShelfCopyHere");

            // IsShelfPanelVisible 갱신 통지 (CollectionChanged → UI 반영)
            ViewModel.NotifyShelfVisibilityChanged();
        }

        // ── Add items to Shelf ──────────────────────────────────

        /// <summary>
        /// 현재 선택된 파일들을 Shelf에 추가한다.
        /// </summary>
        internal void ExecuteShelfAdd()
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return;

            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0) return;

            var paths = selectedItems.Select(i => i.Path).ToList();
            AddPathsToShelf(paths);
        }

        /// <summary>
        /// Shelf 패널 표시/숨김을 토글한다.
        /// </summary>
        internal void ExecuteShelfToggle()
        {
            ViewModel.IsShelfVisible = !ViewModel.IsShelfVisible;
        }

        private void AddPathsToShelf(List<string> paths)
        {
            if (ViewModel.ShelfItems.Count >= ShelfService.MaxShelfItems)
            {
                ViewModel.ShowToast(_loc.Get("ShelfFull"));
                return;
            }

            var newItems = ShelfSvc.CreateShelfItems(paths, ViewModel.ShelfItems);
            foreach (var item in newItems)
            {
                if (ViewModel.ShelfItems.Count >= ShelfService.MaxShelfItems) break;
                ViewModel.ShelfItems.Add(item);
            }

            if (newItems.Count > 0 && !ViewModel.IsShelfVisible)
            {
                ViewModel.IsShelfVisible = true;
            }
        }

        // ── Batch actions ───────────────────────────────────────

        private void OnShelfMoveHereClick(object sender, RoutedEventArgs e)
        {
            ExecuteShelfMoveHere();
        }

        private void OnShelfCopyHereClick(object sender, RoutedEventArgs e)
        {
            ExecuteShelfCopyHere();
        }

        private void OnShelfClearClick(object sender, RoutedEventArgs e)
        {
            ExecuteShelfClear();
        }

        internal void ExecuteShelfMoveHere()
        {
            var currentPath = ViewModel.ActiveExplorer?.CurrentPath;
            if (string.IsNullOrEmpty(currentPath) || ViewModel.ShelfItems.Count == 0) return;

            var paths = ShelfService.GetPaths(ViewModel.ShelfItems);

            var op = new MoveFileOperation(paths, currentPath);
            ViewModel.FileOperationManager.StartOperation(op, DispatcherQueue);

            // 이동 후 Shelf 비우기
            ViewModel.ShelfItems.Clear();
        }

        internal void ExecuteShelfCopyHere()
        {
            var currentPath = ViewModel.ActiveExplorer?.CurrentPath;
            if (string.IsNullOrEmpty(currentPath) || ViewModel.ShelfItems.Count == 0) return;

            var paths = ShelfService.GetPaths(ViewModel.ShelfItems);

            var op = new CopyFileOperation(paths, currentPath);
            ViewModel.FileOperationManager.StartOperation(op, DispatcherQueue);

            // 복사는 Shelf 유지 (사용자가 다른 곳에도 복사할 수 있으므로)
        }

        internal void ExecuteShelfClear()
        {
            ViewModel.ShelfItems.Clear();
        }

        // ── Individual item actions ─────────────────────────────

        private void OnShelfItemRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var item = ViewModel.ShelfItems.FirstOrDefault(s => s.Id == id);
                if (item != null)
                {
                    ViewModel.ShelfItems.Remove(item);
                }
            }
        }

        private void OnShelfItemPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = GetThemeBrush("SpanBgHoverBrush");
            }
        }

        private void OnShelfItemPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = GetThemeBrush("SpanBgLayer1Brush");
            }
        }

        private void OnShelfItemRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // Phase 2: 개별 아이템 컨텍스트 메뉴 (열기, 출처 폴더 이동 등)
        }

        // ── Drag & Drop ─────────────────────────────────────────

        private void OnShelfDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text) ||
                e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;

                // Yoink-style: Shelf 바 하이라이트
                ShelfPanel.Background = GetThemeBrush("SpanBgActiveBrush");
                UpdateDragTooltip(_loc.Get("ShelfDragHint"), e, sender as UIElement ?? (UIElement)Content);
            }
        }

        private async void OnShelfDrop(object sender, DragEventArgs e)
        {
            // Shelf 바 색상 복원
            ShelfPanel.Background = GetThemeBrush("SpanBgLayer2Brush");
            HideDragTooltip();

            try
            {
                List<string>? paths = null;

                // 내부 드래그: SourcePaths 프로퍼티
                if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
                {
                    paths = srcPaths;
                }
                // 외부 드래그: Text 또는 StorageItems
                else if (e.DataView.Contains(StandardDataFormats.Text))
                {
                    var text = await e.DataView.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        paths = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim('\r', '"'))
                            .Where(p => File.Exists(p) || Directory.Exists(p))
                            .ToList();
                    }
                }
                else if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                }

                if (paths != null && paths.Count > 0)
                {
                    AddPathsToShelf(paths);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Shelf] Drop failed: {ex.Message}");
            }
        }

        private void OnShelfDragLeave(object sender, DragEventArgs e)
        {
            // Shelf 바 색상 복원
            ShelfPanel.Background = GetThemeBrush("SpanBgLayer2Brush");
            HideDragTooltip();
        }
    }
}
