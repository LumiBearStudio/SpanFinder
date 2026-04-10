using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Span.Helpers;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// File Shelf — Yoink / DragShelf 스타일 플로팅 오버레이 패널.
    /// 3-상태: Hidden(숨김) → Collapsed(36px 배지) → Full(270px 패널).
    /// 마우스 이탈 시 자동 축소, 진입 시 자동 확장.
    /// </summary>
    public partial class MainWindow
    {
        private ShelfService? _shelfService;
        private bool _shelfAnimating;
        private bool _isDragOverShelf;
        private RubberBandSelectionHelper? _shelfRubberBandHelper;
        private bool _shelfSyncingSelection;
        private DispatcherQueueTimer? _shelfCollapseTimer;
        private long _shelfClosedTick;       // HideShelf 쿨다운
        private bool _shelfIsCollapsed;      // 현재 축소(36px) 상태인지
        private DispatcherQueueTimer? _shelfPointerTracker; // Full 상태에서 커서 위치 폴링

        private const double ShelfFullWidth = 270;
        private const double ShelfCollapsedWidth = 36;
        private const int CollapseDelayMs = 500;    // DragShelf idle timer = 500ms
        private const int CloseCooldownMs = 600;    // 닫기 후 Strip 재반응 방지

        private ShelfService ShelfSvc
            => _shelfService ??= App.Current.Services.GetRequiredService<ShelfService>();

        // ── Initialization ──────────────────────────────────────

        private void InitializeShelf()
        {
            ViewModel.ShelfItems.CollectionChanged += OnShelfItemsCollectionChanged;

            if (!IsShelfEnabled) return;

            LoadShelfFromSettings();
            UpdateShelfUI();
            InitializeShelfRubberBand();

            // 복원된 항목이 있으면 바로 Collapsed(36px) 상태로 시작
            if (ViewModel.ShelfItems.Count > 0)
            {
                _shelfIsCollapsed = true;
                ShelfPanel.Width = ShelfCollapsedWidth;
                ShelfSlideTransform.X = 0;
                ShelfFullContent.Opacity = 0;
                ShelfCollapsedBadge.Visibility = Visibility.Visible;
                UpdateCollapsedBadge();
                ViewModel.IsShelfVisible = true;
                ViewModel.NotifyShelfVisibilityChanged();
            }
        }

        private void LoadShelfFromSettings()
        {
            var restored = ShelfSvc.LoadShelfItems();
            foreach (var item in restored)
            {
                if (ViewModel.ShelfItems.Count >= ShelfService.MaxShelfItems) break;
                ViewModel.ShelfItems.Add(item);
            }
        }

        internal void SaveShelfToSettings()
        {
            var settings = App.Current.Services.GetRequiredService<ISettingsService>();
            if (settings.ShelfSaveEnabled)
                ShelfSvc.SaveShelfItems(ViewModel.ShelfItems);
            else
                ShelfSvc.SaveShelfItems(new System.Collections.ObjectModel.ObservableCollection<ShelfItem>());
        }

        private void InitializeShelfRubberBand()
        {
            if (ShelfListViewGrid == null || ShelfListView == null) return;
            if (_shelfRubberBandHelper != null) return;

            _shelfRubberBandHelper = new RubberBandSelectionHelper(
                ShelfListViewGrid,
                ShelfListView,
                () => _shelfSyncingSelection,
                val => _shelfSyncingSelection = val,
                syncCallback: _ => { },
                afterSyncCallback: null);
        }

        private void OnShelfItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateShelfUI();
            UpdateCollapsedBadge();
        }

        private void UpdateShelfUI()
        {
            var count = ViewModel.ShelfItems.Count;

            ShelfCountText.Text = $"Shelf ({count})";

            var hasItems = count > 0;
            ShelfMoveHereButton.IsEnabled = hasItems;
            ShelfCopyHereButton.IsEnabled = hasItems;

            ShelfMoveText.Text = _loc.Get("ShelfMoveHere");
            ShelfCopyText.Text = _loc.Get("ShelfCopyHere");

            ShelfEmptyHint.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            ShelfListView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

            ViewModel.NotifyShelfVisibilityChanged();
        }

        private void UpdateCollapsedBadge()
        {
            ShelfCollapsedCount.Text = ViewModel.ShelfItems.Count.ToString();
        }

        // ── 3-State: Hidden / Collapsed(36px) / Full(270px) ─────

        /// <summary>전체 패널 표시 (270px). Ctrl+B 또는 아이템 추가 시.</summary>
        private void ShowShelf()
        {
            if (ViewModel.IsShelfPanelVisible == Visibility.Visible
                && ShelfSlideTransform.X == 0
                && !_shelfIsCollapsed)
            {
                StartShelfPointerTracking(); // 이미 Full이면 폴링만 확인
                return;
            }

            _shelfCollapseTimer?.Stop();

            if (_shelfIsCollapsed)
            {
                // Collapsed(36px) → Full(270px) 확장
                ExpandShelf();
                return;
            }

            // Hidden → Full (슬라이드-인)
            ViewModel.IsShelfVisible = true;
            ViewModel.NotifyShelfVisibilityChanged();
            _shelfIsCollapsed = false;
            ShelfFullContent.Opacity = 1;
            ShelfCollapsedBadge.Visibility = Visibility.Collapsed;
            ShelfPanel.Width = ShelfFullWidth;
            AnimateShelfSlide(fromRight: true);
        }

        /// <summary>완전 숨김 (0px, 화면 밖). X 버튼 또는 아이템 없을 때.</summary>
        private void HideShelf()
        {
            _shelfCollapseTimer?.Stop();
            StopShelfPointerTracking();
            ViewModel.IsShelfVisible = false;
            ViewModel.IsShelfDragHover = false;
            _shelfClosedTick = Environment.TickCount64;
            _shelfIsCollapsed = false;
            AnimateShelfSlide(fromRight: false);
        }

        /// <summary>축소 (270px → 36px). 마우스 이탈 시 자동 호출.</summary>
        private void CollapseShelf()
        {
            if (_shelfAnimating || _shelfIsCollapsed) return;
            StopShelfPointerTracking();
            _shelfAnimating = true;
            _shelfIsCollapsed = true;
            _shelfClosedTick = Environment.TickCount64;

            var storyboard = new Storyboard();

            // 콘텐츠 페이드 아웃 (150ms)
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, ShelfFullContent);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            storyboard.Children.Add(fadeOut);

            // 너비 축소 (250ms) — DragShelf Shrink = 250ms
            var shrink = new DoubleAnimation
            {
                From = ShelfFullWidth, To = ShelfCollapsedWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(shrink, ShelfPanel);
            Storyboard.SetTargetProperty(shrink, "Width");
            storyboard.Children.Add(shrink);

            storyboard.Completed += (s, e) =>
            {
                _shelfAnimating = false;
                ShelfCollapsedBadge.Visibility = Visibility.Visible;
                UpdateCollapsedBadge();
            };

            storyboard.Begin();
        }

        /// <summary>확장 (36px → 270px). 축소 상태에서 마우스 진입 시.</summary>
        private void ExpandShelf()
        {
            if (_shelfAnimating || !_shelfIsCollapsed) return;
            _shelfAnimating = true;
            _shelfIsCollapsed = false;

            ShelfCollapsedBadge.Visibility = Visibility.Collapsed;
            ViewModel.IsShelfVisible = true;

            var storyboard = new Storyboard();

            // 너비 확장 (250ms)
            var expand = new DoubleAnimation
            {
                From = ShelfCollapsedWidth, To = ShelfFullWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(expand, ShelfPanel);
            Storyboard.SetTargetProperty(expand, "Width");
            storyboard.Children.Add(expand);

            // 콘텐츠 페이드 인 (200ms, 50ms 딜레이)
            var fadeIn = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, ShelfFullContent);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);

            storyboard.Completed += (s, e) =>
            {
                _shelfAnimating = false;
                StartShelfPointerTracking(); // 확장 완료 → 폴링 시작
            };

            storyboard.Begin();
        }

        private void AnimateShelfSlide(bool fromRight)
        {
            if (_shelfAnimating) return;
            _shelfAnimating = true;

            // Hidden ↔ Full 슬라이드
            ShelfPanel.Width = ShelfFullWidth;
            ShelfFullContent.Opacity = 1;
            ShelfCollapsedBadge.Visibility = Visibility.Collapsed;

            var storyboard = new Storyboard();

            var slideAnim = new DoubleAnimation
            {
                From = fromRight ? ShelfFullWidth : 0,
                To = fromRight ? 0 : ShelfFullWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = fromRight ? EasingMode.EaseOut : EasingMode.EaseIn }
            };
            Storyboard.SetTarget(slideAnim, ShelfSlideTransform);
            Storyboard.SetTargetProperty(slideAnim, "X");
            storyboard.Children.Add(slideAnim);

            storyboard.Completed += (s, e) =>
            {
                _shelfAnimating = false;
                if (fromRight)
                {
                    // Full 상태 진입 완료 → 폴링 시작
                    StartShelfPointerTracking();
                }
                if (!fromRight)
                {
                    ViewModel.NotifyShelfVisibilityChanged();

                    // Hidden 완료 후 아이템 있으면 Collapsed(36px) 상태로 전환
                    if (ViewModel.ShelfItems.Count > 0)
                    {
                        _shelfIsCollapsed = true;
                        ShelfPanel.Width = ShelfCollapsedWidth;
                        ShelfSlideTransform.X = 0;
                        ShelfFullContent.Opacity = 0;
                        ShelfCollapsedBadge.Visibility = Visibility.Visible;
                        UpdateCollapsedBadge();
                        ViewModel.IsShelfVisible = true; // Collapsed도 Visible
                        ViewModel.NotifyShelfVisibilityChanged();
                    }
                }
            };

            storyboard.Begin();
        }

        // ── Toggle / Add / Clear ────────────────────────────────

        private bool IsShelfEnabled
            => App.Current.Services.GetRequiredService<ISettingsService>().ShelfEnabled;

        /// <summary>Settings에서 Shelf ON/OFF 토글 시 런타임 반영.</summary>
        internal void ApplyShelfEnabledSetting(bool enabled)
        {
            if (!enabled)
            {
                // OFF → 즉시 숨기고 타이머/폴링 중지
                _shelfCollapseTimer?.Stop();
                StopShelfPointerTracking();
                _isDragOverShelf = false;

                // 현재 보이는 Shelf 숨기기
                ViewModel.IsShelfVisible = false;
                ViewModel.IsShelfDragHover = false;
                _shelfIsCollapsed = false;
                _shelfAnimating = false;
                ShelfSlideTransform.X = ShelfFullWidth;
                ShelfCollapsedBadge.Visibility = Visibility.Collapsed;
                ViewModel.NotifyShelfVisibilityChanged();
            }
            else
            {
                // ON → 아이템이 있으면 Collapsed(36px) 상태로 복원
                if (ViewModel.ShelfItems.Count > 0)
                {
                    _shelfIsCollapsed = true;
                    ShelfPanel.Width = ShelfCollapsedWidth;
                    ShelfSlideTransform.X = 0;
                    ShelfFullContent.Opacity = 0;
                    ShelfCollapsedBadge.Visibility = Visibility.Visible;
                    UpdateCollapsedBadge();
                    ViewModel.IsShelfVisible = true;
                    ViewModel.NotifyShelfVisibilityChanged();
                }
            }
        }

        internal void ExecuteShelfAdd()
        {
            if (!IsShelfEnabled) return;
            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return;

            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0) return;

            var paths = selectedItems.Select(i => i.Path).ToList();
            AddPathsToShelf(paths);
        }

        internal void ExecuteShelfToggle()
        {
            if (!IsShelfEnabled) return;

            // Collapsed 상태에서 토글하면 확장
            if (_shelfIsCollapsed)
            {
                ExpandShelf();
                return;
            }

            bool isCurrentlyShown = ViewModel.IsShelfPanelVisible == Visibility.Visible
                                    && ShelfSlideTransform.X == 0;

            if (isCurrentlyShown)
                HideShelf();
            else
                ShowShelf();
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

            if (newItems.Count > 0)
            {
                ShowShelf();
            }
        }

        // ── Batch actions ───────────────────────────────────────

        private void OnShelfMoveHereClick(object sender, RoutedEventArgs e) => ExecuteShelfMoveHere();
        private void OnShelfCopyHereClick(object sender, RoutedEventArgs e) => ExecuteShelfCopyHere();
        private void OnShelfClearClick(object sender, RoutedEventArgs e) => ExecuteShelfClear();
        private void OnShelfCloseClick(object sender, RoutedEventArgs e) => HideShelf();

        internal void ExecuteShelfMoveHere()
        {
            var currentPath = ViewModel.ActiveExplorer?.CurrentPath;
            if (string.IsNullOrEmpty(currentPath) || ViewModel.ShelfItems.Count == 0) return;

            var paths = ShelfService.GetPaths(ViewModel.ShelfItems);
            var op = new MoveFileOperation(paths, currentPath);
            ViewModel.FileOperationManager.StartOperation(op, DispatcherQueue);

            var unpinned = ViewModel.ShelfItems.Where(i => !i.IsPinned).ToList();
            foreach (var item in unpinned)
                ViewModel.ShelfItems.Remove(item);

            foreach (var pinned in ViewModel.ShelfItems)
            {
                var newPath = System.IO.Path.Combine(currentPath, System.IO.Path.GetFileName(pinned.Path));
                pinned.Path = newPath;
                pinned.SourceFolder = currentPath;
            }

            if (ViewModel.ShelfItems.Count > 0)
                ViewModel.ShowToast(_loc.Get("ShelfPinnedKept"));
        }

        internal void ExecuteShelfCopyHere()
        {
            var currentPath = ViewModel.ActiveExplorer?.CurrentPath;
            if (string.IsNullOrEmpty(currentPath) || ViewModel.ShelfItems.Count == 0) return;

            var paths = ShelfService.GetPaths(ViewModel.ShelfItems);
            var op = new CopyFileOperation(paths, currentPath);
            ViewModel.FileOperationManager.StartOperation(op, DispatcherQueue);
        }

        internal void ExecuteShelfClear()
        {
            var unpinned = ViewModel.ShelfItems.Where(i => !i.IsPinned).ToList();
            foreach (var item in unpinned)
                ViewModel.ShelfItems.Remove(item);

            if (ViewModel.ShelfItems.Count == 0)
            {
                HideShelf();
            }
            else if (unpinned.Count > 0)
            {
                ViewModel.ShowToast(_loc.Get("ShelfPinnedKept"));
            }
        }

        internal void ExecuteShelfRemoveSelected()
        {
            if (ShelfListView.SelectedItems.Count == 0) return;

            var selected = ShelfListView.SelectedItems.OfType<ShelfItem>().ToList();
            foreach (var item in selected)
                ViewModel.ShelfItems.Remove(item);
        }

        private void OnShelfListViewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                ExecuteShelfRemoveSelected();
                e.Handled = true;
            }
        }

        // ── Double-click to open ────────────────────────────────

        private void OnShelfItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe)
            {
                var item = fe.DataContext as ShelfItem;
                if (item == null && sender is FrameworkElement senderFe)
                    item = senderFe.DataContext as ShelfItem;

                if (item != null)
                {
                    try
                    {
                        var shell = App.Current.Services.GetRequiredService<ShellService>();
                        shell.OpenFile(item.Path);
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[Shelf] Open failed: {ex.Message}");
                    }
                }
            }
        }

        // ── Individual item actions ─────────────────────────────

        private void OnShelfItemRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var item = ViewModel.ShelfItems.FirstOrDefault(s => s.Id == id);
                if (item != null)
                    ViewModel.ShelfItems.Remove(item);
            }
        }

        private void OnShelfItemPinClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var item = ViewModel.ShelfItems.FirstOrDefault(s => s.Id == id);
                if (item != null)
                    item.IsPinned = !item.IsPinned;
            }
        }

        // ── Hover overlay buttons ───────────────────────────────

        private void OnShelfItemPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var buttons = FindChildByTag(grid, "ShelfHoverButtons");
                if (buttons != null) buttons.Visibility = Visibility.Visible;
            }
        }

        private void OnShelfItemPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var buttons = FindChildByTag(grid, "ShelfHoverButtons");
                if (buttons != null) buttons.Visibility = Visibility.Collapsed;
            }
        }

        private static FrameworkElement? FindChildByTag(Grid grid, string tag)
        {
            foreach (var child in grid.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is string t && t == tag)
                    return fe;
            }
            return null;
        }

        // ── Right-click context menu ────────────────────────────

        private void OnShelfItemRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var item = fe.DataContext as ShelfItem;
            if (item == null) return;

            var menu = new MenuFlyout();

            var openCmd = new MenuFlyoutItem
            {
                Text = _loc.Get("ShelfOpen"),
                Icon = new FontIcon { Glyph = "\uE8E5" }
            };
            openCmd.Click += (s, a) =>
            {
                try { App.Current.Services.GetRequiredService<ShellService>().OpenFile(item.Path); }
                catch { }
            };
            menu.Items.Add(openCmd);

            var goToSource = new MenuFlyoutItem
            {
                Text = _loc.Get("ShelfGoToSource"),
                Icon = new FontIcon { Glyph = "\uE838" }
            };
            goToSource.Click += (s, a) =>
            {
                if (!string.IsNullOrEmpty(item.SourceFolder))
                    _ = ViewModel.ActiveExplorer?.NavigateToPath(item.SourceFolder);
            };
            menu.Items.Add(goToSource);

            menu.Items.Add(new MenuFlyoutSeparator());

            var pinCmd = new MenuFlyoutItem
            {
                Text = item.IsPinned ? _loc.Get("ShelfUnpinItem") : _loc.Get("ShelfPinItem"),
                Icon = new FontIcon { Glyph = item.IsPinned ? "\uE77A" : "\uE718" }
            };
            pinCmd.Click += (s, a) => item.IsPinned = !item.IsPinned;
            menu.Items.Add(pinCmd);

            var removeCmd = new MenuFlyoutItem
            {
                Text = _loc.Get("ShelfRemove"),
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            removeCmd.Click += (s, a) => ViewModel.ShelfItems.Remove(item);
            menu.Items.Add(removeCmd);

            menu.ShowAt(fe, e.GetPosition(fe));
        }

        // ── Drag OUT from Shelf ─────────────────────────────────

        private void OnShelfDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (_shelfRubberBandHelper?.IsActive == true)
            { e.Cancel = true; return; }

            var items = e.Items.OfType<ShelfItem>().ToList();
            if (items.Count == 0) { e.Cancel = true; return; }

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = "Shelf";
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;

            var capturedPaths = new List<string>(paths);
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });
        }

        private void OnShelfDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (args.DropResult == DataPackageOperation.Move)
            {
                var movedItems = args.Items.OfType<ShelfItem>().Where(i => !i.IsPinned).ToList();
                foreach (var item in movedItems)
                    ViewModel.ShelfItems.Remove(item);
            }

            // 드래그 완료 후 마우스가 이미 Shelf 밖 → 축소 타이머 시작
            if (ViewModel.ShelfItems.Count > 0 && !_shelfIsCollapsed && !_shelfAnimating)
                StartCollapseTimer();
            else if (ViewModel.ShelfItems.Count == 0)
                HideShelf();
        }

        // ── Drag auto-show (Yoink style) ────────────────────────

        internal void ShowShelfForDrag()
        {
            if (!IsShelfEnabled) return;
            if (!ViewModel.IsShelfDragHover)
            {
                ViewModel.IsShelfDragHover = true;
                ViewModel.NotifyShelfVisibilityChanged();

                if (_shelfIsCollapsed)
                {
                    // Collapsed → Full 확장
                    ExpandShelf();
                }
                else
                {
                    _shelfIsCollapsed = false;
                    ShelfFullContent.Opacity = 1;
                    ShelfCollapsedBadge.Visibility = Visibility.Collapsed;
                    ShelfPanel.Width = ShelfFullWidth;
                    AnimateShelfSlide(fromRight: true);
                }
            }
        }

        internal void TryHideShelfAfterPaneDragLeave()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDragOverShelf) return;
                HideShelfAfterDrag();
            });
        }

        internal void HideShelfAfterDrag()
        {
            if (!ViewModel.IsShelfDragHover) return;

            ViewModel.IsShelfDragHover = false;

            if (ViewModel.ShelfItems.Count > 0)
            {
                // 아이템 있으면 축소 타이머 시작
                if (!_shelfIsCollapsed && !_shelfAnimating)
                    StartCollapseTimer();
                return;
            }

            if (ViewModel.IsShelfVisible)
                return;

            AnimateShelfSlide(fromRight: false);
        }

        // ── Shelf Panel Drag & Drop (외부 → Shelf) ──────────────

        private void OnShelfDragOver(object sender, DragEventArgs e)
        {
            _isDragOverShelf = true;

            if (e.DataView.Contains(StandardDataFormats.Text) ||
                e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                ShelfPanel.BorderBrush = GetThemeBrush("SpanAccentBrush");
                ShelfPanel.BorderThickness = new Thickness(2, 0, 0, 0);
            }
        }

        private async void OnShelfDrop(object sender, DragEventArgs e)
        {
            ShelfPanel.BorderBrush = GetThemeBrush("SpanBorderSubtleBrush");
            ShelfPanel.BorderThickness = new Thickness(1, 0, 0, 0);

            try
            {
                List<string>? paths = null;

                if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
                    paths = srcPaths;
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
                    AddPathsToShelf(paths);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Shelf] Drop failed: {ex.Message}");
            }

            _isDragOverShelf = false;
            // 드롭 완료 후 마우스가 밖이면 축소
            if (ViewModel.ShelfItems.Count > 0 && !_shelfIsCollapsed && !_shelfAnimating)
                StartCollapseTimer();
        }

        private void OnShelfDragLeave(object sender, DragEventArgs e)
        {
            _isDragOverShelf = false;
            ShelfPanel.BorderBrush = GetThemeBrush("SpanBorderSubtleBrush");
            ShelfPanel.BorderThickness = new Thickness(1, 0, 0, 0);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDragOverShelf) return;
                if (ViewModel.ShelfItems.Count > 0 || ViewModel.IsShelfVisible) return;
                HideShelfAfterDrag();
            });
        }

        // ── Auto-collapse / expand — 폴링 + PointerEntered ──────

        /// <summary>Full(270px) 상태에서 커서가 ShelfPanel 밖인지 주기적으로 체크.</summary>
        private void StartShelfPointerTracking()
        {
            if (_shelfPointerTracker != null) return;
            _shelfPointerTracker = DispatcherQueue.CreateTimer();
            _shelfPointerTracker.Interval = TimeSpan.FromMilliseconds(200);
            _shelfPointerTracker.IsRepeating = true;
            _shelfPointerTracker.Tick += OnShelfPointerTrackerTick;
            _shelfPointerTracker.Start();
        }

        private void StopShelfPointerTracking()
        {
            if (_shelfPointerTracker == null) return;
            _shelfPointerTracker.Stop();
            _shelfPointerTracker.Tick -= OnShelfPointerTrackerTick;
            _shelfPointerTracker = null;
        }

        private void OnShelfPointerTrackerTick(DispatcherQueueTimer sender, object args)
        {
            // Full 상태가 아니면 중지
            if (_shelfIsCollapsed || _shelfAnimating || ViewModel.ShelfItems.Count == 0)
            {
                StopShelfPointerTracking();
                return;
            }

            // 드래그 중에는 축소하지 않음
            if (_isDragOverShelf) return;

            if (!IsCursorOverShelfPanel())
            {
                StopShelfPointerTracking();
                StartCollapseTimer();
            }
        }

        private bool IsCursorOverShelfPanel()
        {
            try
            {
                if (!Helpers.NativeMethods.GetCursorPos(out var screenPt)) return true;

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var clientPt = screenPt;
                Helpers.NativeMethods.ScreenToClient(hwnd, ref clientPt);

                var scale = ShelfPanel.XamlRoot?.RasterizationScale ?? 1.0;
                double cx = clientPt.X / scale;
                double cy = clientPt.Y / scale;

                var transform = ShelfPanel.TransformToVisual(null);
                var bounds = transform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0, ShelfPanel.ActualWidth, ShelfPanel.ActualHeight));

                return cx >= bounds.X && cx <= bounds.X + bounds.Width
                    && cy >= bounds.Y && cy <= bounds.Y + bounds.Height;
            }
            catch
            {
                return true; // fail-safe: 에러 시 축소하지 않음
            }
        }

        private void OnShelfPanelPointerExited(object sender, PointerRoutedEventArgs e)
        {
            // 폴링이 주력이지만, XAML 이벤트가 먹히면 빠르게 반응
            if (_shelfAnimating || _shelfIsCollapsed) return;
            if (_isDragOverShelf) return;
            if (ViewModel.ShelfItems.Count == 0) return;

            StopShelfPointerTracking();
            StartCollapseTimer();
        }

        private void OnShelfPanelPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // 축소 대기 타이머 취소
            _shelfCollapseTimer?.Stop();

            // Collapsed 상태에서 마우스 진입 → 확장
            if (_shelfIsCollapsed && !_shelfAnimating)
            {
                if (Environment.TickCount64 - _shelfClosedTick < CloseCooldownMs) return;
                ExpandShelf();
            }
            else if (!_shelfIsCollapsed && !_shelfAnimating)
            {
                // Full 상태에서 다시 진입 → 폴링 재시작
                StartShelfPointerTracking();
            }
        }

        private void StartCollapseTimer()
        {
            _shelfCollapseTimer?.Stop();
            _shelfCollapseTimer = DispatcherQueue.CreateTimer();
            _shelfCollapseTimer.Interval = TimeSpan.FromMilliseconds(CollapseDelayMs);
            _shelfCollapseTimer.IsRepeating = false;
            _shelfCollapseTimer.Tick += (s, a) =>
            {
                DebugLogger.Log($"[Shelf] Collapse timer fired! collapsed={_shelfIsCollapsed}, items={ViewModel.ShelfItems.Count}");
                if (!_shelfIsCollapsed && !_shelfAnimating && ViewModel.ShelfItems.Count > 0)
                    CollapseShelf();
            };
            _shelfCollapseTimer.Start();
        }
    }
}
