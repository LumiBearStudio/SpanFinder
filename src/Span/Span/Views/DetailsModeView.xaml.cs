using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace Span.Views
{
    public sealed partial class DetailsModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }
        public IntPtr OwnerHwnd { get; set; }
        public bool IsRightPane { get; set; }

        private ExplorerViewModel? _viewModel;
        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                _viewModel = value;
                RootGrid.DataContext = _viewModel;

                if (_viewModel != null && _isLoaded)
                {
                    SortItems(_currentSortBy, _isAscending);
                }
            }
        }

        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isLoaded = false;
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        // Current column widths (read from header ColumnDefinitions)
        private double _dateColumnWidth = 200;
        private double _typeColumnWidth = 150;
        private double _sizeColumnWidth = 100;

        // Callback tokens for ColumnDefinition.WidthProperty change tracking
        private long _dateCallbackToken;
        private long _typeCallbackToken;
        private long _sizeCallbackToken;

        // Guard against double cleanup (Cleanup() from OnClosed + OnUnloaded from visual tree teardown)
        private bool _isCleanedUp = false;

        public DetailsModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                _isCleanedUp = false; // Allow cleanup on next Unloaded

                // Get ViewModel from MainWindow's DataContext
                if (this.XamlRoot?.Content is FrameworkElement root &&
                    root.DataContext is MainViewModel mainVm)
                {
                    ViewModel = IsRightPane ? mainVm.RightExplorer : mainVm.Explorer;
                }

                // Restore sort settings
                RestoreSortSettings();

                // Subscribe to ColumnDefinition.Width changes via RegisterPropertyChangedCallback.
                // CRITICAL: HeaderGrid.SizeChanged does NOT fire when GridSplitter rearranges
                // internal columns — the Grid's total size stays the same. We must watch
                // each ColumnDefinition individually.
                _dateCallbackToken = DateColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _typeCallbackToken = TypeColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _sizeCallbackToken = SizeColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save sort settings (always, even if already cleaned up)
                SaveSortSettings();

                // Skip if Cleanup() was already called from MainWindow.OnClosed
                if (_isCleanedUp) return;

                PerformCleanup();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.OnUnloaded] Error: {ex.Message}");
            }
        }

        #region Column Width Synchronization

        /// <summary>
        /// Fired when any of the 3 resizable ColumnDefinitions change Width
        /// (via GridSplitter drag or window resize).
        ///
        /// WinUI 3 bugs that forced this approach:
        /// 1. ColumnDefinition.Width binding in DataTemplate doesn't respond
        ///    to INotifyPropertyChanged (Microsoft Issue #10300).
        /// 2. Grid.SizeChanged does NOT fire when GridSplitter rearranges
        ///    internal columns (total Grid size stays the same).
        ///
        /// Solution: RegisterPropertyChangedCallback on each ColumnDefinition,
        /// then set Border.Width directly on each cell element.
        /// </summary>
        private void OnColumnWidthChanged(DependencyObject sender, DependencyProperty dp)
        {
            _dateColumnWidth = DateColumnDef.ActualWidth;
            _typeColumnWidth = TypeColumnDef.ActualWidth;
            _sizeColumnWidth = SizeColumnDef.ActualWidth;

            UpdateAllVisibleContainerWidths();
        }

        /// <summary>
        /// Called when ListView containers are created or recycled.
        /// Sets cell widths to match current header column widths.
        /// This handles virtualization: newly realized containers get correct widths.
        /// </summary>
        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            if (args.ItemContainer?.ContentTemplateRoot is Grid grid)
            {
                ApplyCellWidths(grid);
            }
        }

        /// <summary>
        /// Apply current column widths to a single item Grid's cell Borders.
        /// Note: FindName() does NOT work inside DataTemplate namescope in WinUI 3.
        /// Instead, we find Border elements by their Grid.Column attached property.
        /// </summary>
        private void ApplyCellWidths(Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border border)
                {
                    int col = Grid.GetColumn(border);
                    switch (col)
                    {
                        case 3: border.Width = _dateColumnWidth; break;  // Date
                        case 5: border.Width = _typeColumnWidth; break;  // Type
                        case 7: border.Width = _sizeColumnWidth; break;  // Size
                    }
                }
            }
        }

        /// <summary>
        /// Update all currently visible containers' cell widths.
        /// Only iterates realized containers (typically 20-50 items), not all items.
        /// </summary>
        private void UpdateAllVisibleContainerWidths()
        {
            if (DetailsListView?.ItemsPanelRoot == null) return;

            for (int i = 0; i < DetailsListView.Items.Count; i++)
            {
                if (DetailsListView.ContainerFromIndex(i) is ListViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    ApplyCellWidths(grid);
                }
            }
        }

        #endregion

        #region Selection Sync

        private void OnDetailsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is ListView listView)
            {
                ViewModel.CurrentFolder.SyncSelectedItems(listView.SelectedItems);
            }
        }

        #endregion

        #region Item Interaction

        private void OnDragItemsStarting(object sender, Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e)
        {
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0) { e.Cancel = true; return; }

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = IsRightPane ? "Right" : "Left";
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                                      | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }

        private void OnItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && ContextMenuService != null && ContextMenuHost != null)
            {
                Microsoft.UI.Xaml.Controls.MenuFlyout? flyout = null;

                if (grid.DataContext is FolderViewModel folder)
                    flyout = ContextMenuService.BuildFolderMenu(folder, ContextMenuHost);
                else if (grid.DataContext is FileViewModel file)
                    flyout = ContextMenuService.BuildFileMenu(file, ContextMenuHost);

                if (flyout != null)
                {
                    flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = e.GetPosition(grid)
                    });
                    e.Handled = true;
                }
            }
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[DetailsModeView] DoubleClick: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[DetailsModeView] DoubleClick: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[DetailsModeView] Error opening file: {ex.Message}");
                }
            }
        }

        #endregion

        #region Keyboard Navigation

        private void OnDetailsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Check for rename mode
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected != null && selected.IsRenaming) return;

            // Check for Ctrl/Alt modifiers (let global handlers handle them)
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl || alt) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    HandleDetailsEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    Helpers.DebugLogger.Log("[DetailsModeView] Backspace: Navigating to parent folder");
                    break;

                case Windows.System.VirtualKey.Delete:
                case Windows.System.VirtualKey.F2:
                    // Let global handler handle these
                    break;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                    // ListView handles Up/Down navigation by default
                    break;
            }
        }

        private void HandleDetailsEnter()
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[DetailsModeView] Enter: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[DetailsModeView] Enter: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[DetailsModeView] Error opening file: {ex.Message}");
                }
            }
        }

        #endregion

        #region Sorting

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string sortBy)
            {
                if (_currentSortBy == sortBy)
                {
                    _isAscending = !_isAscending;
                }
                else
                {
                    _currentSortBy = sortBy;
                    _isAscending = true;
                }

                SortItems(sortBy, _isAscending);
                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted by {sortBy} ({(_isAscending ? "Asc" : "Desc")})");
            }
        }

        private void SortItems(string sortBy, bool ascending)
        {
            if (ViewModel?.CurrentFolder == null || ViewModel.CurrentFolder.Children.Count == 0)
                return;

            var column = ViewModel.CurrentFolder;
            var savedSelection = column.SelectedChild;
            column.IsSorting = true;

            try
            {
                System.Collections.Generic.IEnumerable<FileSystemViewModel> sorted;

                switch (sortBy)
                {
                    case "Name":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                        break;
                    case "DateModified":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.DateModifiedValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.DateModifiedValue);
                        break;
                    case "Type":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.FileType)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.FileType);
                        break;
                    case "Size":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.SizeValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.SizeValue);
                        break;
                    default:
                        return;
                }

                var sortedList = sorted.ToList();
                column.Children.Clear();
                foreach (var item in sortedList)
                {
                    column.Children.Add(item);
                }

                if (savedSelection != null)
                {
                    column.SelectedChild = savedSelection;
                }

                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted {sortedList.Count} items");
            }
            finally
            {
                column.IsSorting = false;
            }
        }

        #endregion

        #region Sort Settings Persistence

        private void SaveSortSettings()
        {
            try
            {
                var composite = new ApplicationDataCompositeValue
                {
                    ["SortColumn"] = _currentSortBy,
                    ["SortAscending"] = _isAscending
                };
                _localSettings.Values["DetailsViewSort"] = composite;
                Helpers.DebugLogger.Log("[DetailsModeView] Sort settings saved");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error saving sort settings: {ex.Message}");
            }
        }

        private void RestoreSortSettings()
        {
            try
            {
                if (_localSettings.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite)
                {
                    if (composite.TryGetValue("SortColumn", out var sortObj) && sortObj is string sortColumn)
                    {
                        _currentSortBy = sortColumn;
                    }
                    if (composite.TryGetValue("SortAscending", out var ascObj) && ascObj is bool ascending)
                    {
                        _isAscending = ascending;
                    }

                    SortItems(_currentSortBy, _isAscending);
                    Helpers.DebugLogger.Log("[DetailsModeView] Sort settings restored");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error restoring sort settings: {ex.Message}");
            }
        }

        #endregion

        #region Focus Management

        /// <summary>
        /// Focus the Details ListView (called from MainWindow on view switch)
        /// </summary>
        public void FocusDataGrid()
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }

        public void FocusListView() => FocusDataGrid();

        /// <summary>
        /// Empty space click → focus ListView → triggers GotFocus bubbling → ActivePane set
        /// </summary>
        private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }

        private void OnEmptyAreaRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.Handled) return; // Item handler already handled
            if (ContextMenuService == null || ContextMenuHost == null) return;

            var folderPath = ViewModel?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(folderPath)) return;

            var flyout = ContextMenuService.BuildEmptyAreaMenu(folderPath, ContextMenuHost);
            flyout.ShowAt(RootGrid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = e.GetPosition(RootGrid)
            });
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// CRITICAL: Called from MainWindow.OnClosed BEFORE visual tree teardown.
        /// Prevents WinUI crash by disconnecting bindings early.
        /// </summary>
        public void Cleanup()
        {
            if (_isCleanedUp) return;
            SaveSortSettings();
            PerformCleanup();
        }

        private void PerformCleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView] Starting cleanup...");

                // Unregister column width callbacks (only if Loaded fired and registered them)
                if (_isLoaded)
                {
                    DateColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _dateCallbackToken);
                    TypeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _typeCallbackToken);
                    SizeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _sizeCallbackToken);
                }

                if (DetailsListView != null)
                {
                    DetailsListView.DoubleTapped -= OnItemDoubleClick;
                    DetailsListView.KeyDown -= OnDetailsKeyDown;
                    DetailsListView.ContainerContentChanging -= OnContainerContentChanging;
                    DetailsListView.ItemsSource = null;
                    DetailsListView.SelectedItem = null;
                }

                _viewModel = null;
                RootGrid.DataContext = null;

                Helpers.DebugLogger.Log("[DetailsModeView] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}
