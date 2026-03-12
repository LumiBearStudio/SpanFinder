using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Span.Helpers;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Views
{
    /// <summary>
    /// Icon 뷰 모드 UserControl.
    /// 파일/폴더를 아이콘 그리드(Small/Medium/Large/ExtraLarge)로 표시한다.
    /// 러버 밴드 선택, 드래그 앤 드롭, 컨텍스트 메뉴, 키보드 내비게이션,
    /// 밀도 설정, 그룹화 기능을 포함한다.
    /// </summary>
    public sealed partial class IconModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }
        public IntPtr OwnerHwnd { get; set; }
        public bool IsRightPane { get; set; }

        /// <summary>
        /// true면 Loaded에서 auto-resolve 건너뜀 (코드에서 ViewModel을 직접 설정한 인스턴스).
        /// XAML 정의 인스턴스(첫 번째 탭)는 false (기본값) → 기존 동작 유지.
        /// </summary>
        public bool IsManualViewModel { get; set; }

        private ExplorerViewModel? _viewModel;
        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                _viewModel = value;
                RootGrid.DataContext = _viewModel;
            }
        }

        private ViewMode _currentIconSize = ViewMode.IconMedium;
        private bool _isLoaded = false;
        private bool _isCleanedUp = false;
        private SettingsService? _settings;

        // Rubber-band selection
        private Helpers.RubberBandSelectionHelper? _rubberBandHelper;
        private bool _isSyncingSelection;

        public IconModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                _isCleanedUp = false; // Allow cleanup on next Unloaded

                if (!IsManualViewModel)
                {
                    // Get ViewModel from MainWindow's DataContext (XAML 정의 인스턴스용)
                    if (this.XamlRoot?.Content is FrameworkElement root &&
                        root.DataContext is MainViewModel mainVm)
                    {
                        ViewModel = IsRightPane ? mainVm.RightExplorer : mainVm.Explorer;
                        UpdateIconSize(mainVm.CurrentIconSize);
                    }
                }

                // Apply ShowCheckboxes setting
                try
                {
                    _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
                    if (_settings != null)
                    {
                        ApplyCheckboxMode(_settings.ShowCheckboxes);
                        _settings.SettingChanged += OnSettingChanged;
                    }
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[IconModeView] Loaded init error: {ex.Message}"); }
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isCleanedUp) return;
                PerformCleanup();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[IconModeView.OnUnloaded] Error: {ex.Message}");
            }
        }

        public void UpdateIconSize(ViewMode iconSize)
        {
            if (!_isLoaded || !Helpers.ViewModeExtensions.IsIconMode(iconSize))
                return;

            _currentIconSize = iconSize;

            // Switch template based on icon size
            string templateKey = iconSize switch
            {
                ViewMode.IconSmall => "SmallIconTemplate",
                ViewMode.IconMedium => "MediumIconTemplate",
                ViewMode.IconLarge => "LargeIconTemplate",
                ViewMode.IconExtraLarge => "ExtraLargeIconTemplate",
                _ => "MediumIconTemplate"
            };

            if (this.Resources.ContainsKey(templateKey))
            {
                IconGridView.ItemTemplate = (DataTemplate)this.Resources[templateKey];
                Helpers.DebugLogger.Log($"[IconModeView] Icon size updated: {Helpers.ViewModeExtensions.GetDisplayName(iconSize)} (Template: {templateKey})");
            }
        }

        private void ApplyCheckboxMode(bool showCheckboxes)
        {
            if (IconGridView == null) return;
            IconGridView.SelectionMode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;
        }

        private void OnSettingChanged(string key, object? value)
        {
            if (key == "ShowCheckboxes" && value is bool show)
            {
                DispatcherQueue.TryEnqueue(() => ApplyCheckboxMode(show));
            }
        }

        private void OnIconSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return;
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is GridView gridView)
            {
                ViewModel.CurrentFolder.SyncSelectedItems(gridView.SelectedItems);
            }
        }

        private void OnDragItemsStarting(object sender, Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e)
        {
            if (_rubberBandHelper?.IsActive == true)
            { e.Cancel = true; return; }

            if (!Helpers.ViewDragDropHelper.SetupDragData(e, IsRightPane))
                e.Cancel = true;
        }

        private async void OnItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            try
            {
                if (_settings != null && !_settings.ShowContextMenu) return;
                if (e.OriginalSource is not FrameworkElement fe || ContextMenuService == null || ContextMenuHost == null)
                    return;

                // OriginalSource가 Image/FontIcon 등 내부 요소일 수 있으므로
                // 안전한 ShowAt 타겟을 찾는다 (GridViewItem 컨테이너 또는 템플릿 루트 Grid)
                var showAtTarget = FindShowAtTarget(fe) ?? fe;
                var position = e.GetPosition(showAtTarget);

                // Check if this is actually a file/folder item (not empty area)
                bool isItem = fe.DataContext is FolderViewModel || fe.DataContext is FileViewModel;
                if (isItem)
                    e.Handled = true; // Prevent bubbling during await

                Microsoft.UI.Xaml.Controls.MenuFlyout? flyout = null;

                if (fe.DataContext is FolderViewModel folder)
                    flyout = await ContextMenuService.BuildFolderMenuAsync(folder, ContextMenuHost);
                else if (fe.DataContext is FileViewModel file)
                    flyout = await ContextMenuService.BuildFileMenuAsync(file, ContextMenuHost);

                if (flyout != null)
                {
                    flyout.ShowAt(showAtTarget, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = position
                    });
                }
                else
                {
                    // Empty area fallback
                    var folderPath = ViewModel?.CurrentFolder?.Path;
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        flyout = ContextMenuService.BuildEmptyAreaMenu(folderPath, ContextMenuHost);
                        flyout.ShowAt(showAtTarget, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                        {
                            Position = position
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[IconModeView] OnItemRightTapped error: {ex.Message}");
            }
        }

        /// <summary>
        /// OriginalSource에서 안전한 ShowAt 타겟 요소를 찾는다.
        /// Image/FontIcon 등 내부 요소 대신 GridViewItem 또는 템플릿 루트 Grid를 반환.
        /// BitmapImage 로딩 중인 Image 컨트롤에 ShowAt하면 E_INVALIDARG 발생 가능.
        /// </summary>
        private static FrameworkElement? FindShowAtTarget(FrameworkElement source)
        {
            var current = source;
            while (current != null)
            {
                if (current is Microsoft.UI.Xaml.Controls.GridViewItem)
                    return current;
                // 템플릿 루트 Grid (DataContext가 ViewModel인 첫 번째 Grid)
                if (current is Grid grid && grid.DataContext is ViewModels.FileSystemViewModel)
                    return grid;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return null;
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            Helpers.ViewItemHelper.OpenFileOrFolder(ViewModel, "IconModeView");
        }

        private void OnIconKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected != null && selected.IsRenaming) return;
            if (Helpers.ViewItemHelper.HasModifierKey()) return;

            // _justFinishedRename 가드: rename 후 Enter가 파일 실행되지 않도록
            if (_justFinishedRename)
            {
                _justFinishedRename = false;
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    return;
                }
            }

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    Helpers.ViewItemHelper.OpenFileOrFolder(ViewModel, "IconModeView");
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Back:
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    break;
            }
        }

        #region Selection Operations

        internal void SelectAll()
        {
            IconGridView?.SelectAll();
        }

        internal void SelectNone()
        {
            if (IconGridView == null) return;
            _isSyncingSelection = true;
            try
            {
                IconGridView.SelectedItems.Clear();
                if (ViewModel?.CurrentFolder != null)
                {
                    ViewModel.CurrentFolder.SelectedChild = null;
                    ViewModel.CurrentFolder.SelectedItems.Clear();
                }
            }
            finally { _isSyncingSelection = false; }
        }

        private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            IconGridView?.Focus(FocusState.Programmatic);
        }

        internal void InvertSelection()
        {
            if (IconGridView == null || ViewModel?.CurrentFolder == null) return;
            var allItems = ViewModel.CurrentFolder.Children.ToList();
            var selectedIndices = new HashSet<int>();
            foreach (var item in IconGridView.SelectedItems)
            {
                int idx = allItems.IndexOf(item as FileSystemViewModel);
                if (idx >= 0) selectedIndices.Add(idx);
            }
            _isSyncingSelection = true;
            try
            {
                IconGridView.SelectedItems.Clear();
                for (int i = 0; i < allItems.Count; i++)
                {
                    if (!selectedIndices.Contains(i))
                        IconGridView.SelectedItems.Add(allItems[i]);
                }
            }
            finally { _isSyncingSelection = false; }
        }

        #endregion

        #region F2 Inline Rename

        private int _renameSelectionCycle;
        private string? _renameTargetPath;
        private bool _justFinishedRename;

        /// <summary>
        /// F2 이름 변경 핸들러. MainWindow.HandleRename()에서 위임됨.
        /// </summary>
        internal void HandleRename()
        {
            var folder = ViewModel?.CurrentFolder;
            if (folder == null) return;

            var selected = folder.SelectedChild;
            if (selected == null && folder.Children.Count > 0)
            {
                selected = folder.Children[0];
                folder.SelectedChild = selected;
            }
            if (selected == null) return;

            var itemPath = (selected as FolderViewModel)?.Path ?? (selected as FileViewModel)?.Path;
            if (selected.IsRenaming && itemPath == _renameTargetPath)
            {
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => FocusIconRenameTextBox());
                return;
            }

            _renameSelectionCycle = 0;
            _renameTargetPath = itemPath;
            selected.BeginRename();

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => FocusIconRenameTextBox());
        }

        private void FocusIconRenameTextBox()
        {
            var folder = ViewModel?.CurrentFolder;
            if (folder?.SelectedChild == null || IconGridView == null) return;

            int idx = folder.Children.IndexOf(folder.SelectedChild);
            if (idx < 0) return;

            var container = IconGridView.ContainerFromIndex(idx) as UIElement;
            if (container == null)
            {
                IconGridView.ScrollIntoView(folder.SelectedChild);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var retryContainer = IconGridView.ContainerFromIndex(idx) as UIElement;
                    if (retryContainer != null)
                    {
                        var tb = VisualTreeHelpers.FindChild<TextBox>(retryContainer as DependencyObject);
                        if (tb != null) ViewRenameHelper.ApplyRenameSelection(tb, folder.SelectedChild is FolderViewModel, _renameSelectionCycle, DispatcherQueue);
                    }
                });
                return;
            }

            var textBox = VisualTreeHelpers.FindChild<TextBox>(container as DependencyObject);
            if (textBox != null)
                ViewRenameHelper.ApplyRenameSelection(textBox, folder.SelectedChild is FolderViewModel, _renameSelectionCycle, DispatcherQueue);
        }

        private void OnRenameTextBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                vm.CommitRename();
                _justFinishedRename = true;
                _renameTargetPath = null;
                e.Handled = true;
                FocusSelectedGridViewItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                vm.CancelRename();
                _justFinishedRename = true;
                _renameTargetPath = null;
                e.Handled = true;
                FocusSelectedGridViewItem();
            }
            else if (e.Key == Windows.System.VirtualKey.F2)
            {
                // F2 cycling while renaming
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                ViewRenameHelper.ApplyRenameSelection(textBox, vm is FolderViewModel, _renameSelectionCycle, DispatcherQueue);
                e.Handled = true;
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            if (vm.IsRenaming)
                vm.CommitRename();
            _justFinishedRename = true;
            _renameTargetPath = null;
        }

        private void FocusSelectedGridViewItem()
        {
            var folder = ViewModel?.CurrentFolder;
            if (folder?.SelectedChild == null || IconGridView == null) return;

            int idx = folder.Children.IndexOf(folder.SelectedChild);
            if (idx < 0) return;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var container = IconGridView.ContainerFromIndex(idx) as UIElement;
                container?.Focus(FocusState.Keyboard);
            });
        }

        #endregion

        // ── Rubber Band Selection ──

        private void OnRootGridLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid || _rubberBandHelper != null) return;

            _rubberBandHelper = new Helpers.RubberBandSelectionHelper(
                grid,
                IconGridView,
                () => _isSyncingSelection,
                val => _isSyncingSelection = val,
                items => ViewModel?.CurrentFolder?.SyncSelectedItems(items));
        }

        private void OnRootGridUnloaded(object sender, RoutedEventArgs e)
        {
            _rubberBandHelper?.Detach();
            _rubberBandHelper = null;
        }

        // Ctrl+Mouse Wheel view mode cycling is handled globally by MainWindow.OnGlobalPointerWheelChanged

        public void ApplyDensity(string density)
        {
            // 숫자 문자열(0~6) 또는 레거시 이름 지원
            int level = density switch
            {
                "compact" => 0,
                "comfortable" => 2,
                "spacious" => 4,
                _ => int.TryParse(density, out var n) ? Math.Clamp(n, 0, 6) : 2
            };
            var margin = new Thickness(2 + level);

            if (IconGridView != null)
            {
                var baseStyle = (Style)Application.Current.Resources["ListViewItemStyle"];
                var style = new Style(typeof(GridViewItem)) { BasedOn = baseStyle };
                style.Setters.Add(new Setter(GridViewItem.MarginProperty, margin));
                style.Setters.Add(new Setter(GridViewItem.MinHeightProperty, 0.0));
                IconGridView.ItemContainerStyle = style;
            }
        }

        // ── Group By ──
        private string _currentGroupBy = "None";

        public void ApplyGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;
            RebuildGroupedItems();
        }

        private void RebuildGroupedItems()
        {
            if (ViewModel?.CurrentFolder == null) return;

            var items = ViewModel.CurrentItems;
            if (items == null) return;

            if (_currentGroupBy == "None" || string.IsNullOrEmpty(_currentGroupBy))
            {
                // 그룹 해제 — 원래 바인딩 복원
                IconGridView.ItemsSource = null;
                IconGridView.SetBinding(
                    GridView.ItemsSourceProperty,
                    new Microsoft.UI.Xaml.Data.Binding
                    {
                        Path = new PropertyPath("CurrentItems"),
                        Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
                    });
                return;
            }

            var groups = items
                .GroupBy(item => Helpers.GroupByHelper.GetGroupKey(item, _currentGroupBy))
                .OrderBy(g => g.Key)
                .Select(g => new Helpers.ItemGroup(g.Key + " (" + g.Count() + ")", g))
                .ToList();

            var cvs = new Microsoft.UI.Xaml.Data.CollectionViewSource
            {
                Source = groups,
                IsSourceGrouped = true
            };
            IconGridView.ItemsSource = cvs.View;
        }

        /// <summary>
        /// On-demand cloud state injection for visible items.
        /// </summary>
        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            if (args.Item is ViewModels.FileSystemViewModel fsVm)
            {
                _viewModel?.CurrentFolder?.InjectCloudStateIfNeeded(fsVm);
            }
        }

        /// <summary>
        /// Focus the Icon GridView (called from MainWindow on view switch)
        /// </summary>
        public void FocusGridView()
        {
            IconGridView?.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// CRITICAL: Cleanup called from MainWindow.OnClosed BEFORE views are unloaded
        /// This prevents WinUI crash by disconnecting bindings early
        /// </summary>
        public void Cleanup()
        {
            if (_isCleanedUp) return;
            PerformCleanup();
        }

        private void PerformCleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            try
            {
                Helpers.DebugLogger.Log("[IconModeView] Starting cleanup...");

                _rubberBandHelper?.Detach();
                _rubberBandHelper = null;

                if (_settings != null)
                {
                    _settings.SettingChanged -= OnSettingChanged;
                    _settings = null;
                }

                if (IconGridView != null)
                {
                    IconGridView.DoubleTapped -= OnItemDoubleClick;
                    IconGridView.KeyDown -= OnIconKeyDown;
                    IconGridView.ItemsSource = null;
                    IconGridView.SelectedItem = null;
                }

                _viewModel = null;
                RootGrid.DataContext = null;

                Helpers.DebugLogger.Log("[IconModeView] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[IconModeView] Cleanup error: {ex.Message}");
            }
        }

    }
}
