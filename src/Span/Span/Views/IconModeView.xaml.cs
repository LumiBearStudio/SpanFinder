using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Views
{
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
                catch { }
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
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (e.OriginalSource is FrameworkElement fe && ContextMenuService != null && ContextMenuHost != null)
            {
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
                    flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = e.GetPosition(fe)
                    });
                }
                else
                {
                    // Empty area fallback
                    var folderPath = ViewModel?.CurrentFolder?.Path;
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        flyout = ContextMenuService.BuildEmptyAreaMenu(folderPath, ContextMenuHost);
                        flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                        {
                            Position = e.GetPosition(fe)
                        });
                    }
                }
            }
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
            var margin = density switch
            {
                "compact" => new Thickness(2),
                "spacious" => new Thickness(8),
                _ => new Thickness(4)
            };

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
