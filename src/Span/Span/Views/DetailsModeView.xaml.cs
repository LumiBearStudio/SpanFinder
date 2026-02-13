using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;
using System.Linq;
using CommunityToolkit.WinUI.UI.Controls;
using System;
using System.Collections.Generic;
using Windows.Storage;

namespace Span.Views
{
    public sealed partial class DetailsModeView : UserControl
    {
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
                    SortItems("Name", true);
                }
            }
        }

        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isLoaded = false;
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public DetailsModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;

                // Get ViewModel from MainWindow's DataContext
                if (this.XamlRoot?.Content is FrameworkElement root &&
                    root.DataContext is MainViewModel mainVm)
                {
                    ViewModel = mainVm.Explorer;
                }

                // Restore column settings
                RestoreColumnSettings();
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView.OnUnloaded] Starting cleanup...");

                // Save column settings only if DataGrid is still valid
                if (DetailsDataGrid != null && DetailsDataGrid.Columns != null)
                {
                    SaveColumnSettings();
                }

                // Disconnect DataGrid events
                if (DetailsDataGrid != null)
                {
                    DetailsDataGrid.Sorting -= OnDataGridSorting;
                    DetailsDataGrid.ColumnReordered -= OnColumnReordered;
                    DetailsDataGrid.DoubleTapped -= OnItemDoubleClick;
                    DetailsDataGrid.KeyDown -= OnDetailsKeyDown;

                    // Clear bindings to prevent memory leaks
                    DetailsDataGrid.ItemsSource = null;
                    DetailsDataGrid.SelectedItem = null;
                }

                // Clear ViewModel reference
                ViewModel = null;

                // Unsubscribe from events
                this.Unloaded -= OnUnloaded;

                Helpers.DebugLogger.Log("[DetailsModeView.OnUnloaded] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.OnUnloaded] Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DetailsModeView.OnUnloaded] Stack: {ex.StackTrace}");
            }
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[DetailsModeView] DoubleClick: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
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
                    // Navigate to parent folder
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    Helpers.DebugLogger.Log("[DetailsModeView] Backspace: Navigating to parent folder");
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Let global handler handle Delete
                    break;

                case Windows.System.VirtualKey.F2:
                    // Let global handler handle F2 (Rename)
                    break;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                    // DataGrid handles Up/Down navigation by default
                    break;
            }
        }

        private void HandleDetailsEnter()
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[DetailsModeView] Enter: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
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

        private void OnDataGridSorting(object sender, DataGridColumnEventArgs e)
        {
            // Get column tag to determine sort property
            string sortBy = e.Column.Tag?.ToString() ?? "Name";

            // Toggle sort direction if same column
            if (_currentSortBy == sortBy)
            {
                _isAscending = !_isAscending;
            }
            else
            {
                _currentSortBy = sortBy;
                _isAscending = true;
            }

            // Apply sort
            SortItems(sortBy, _isAscending);

            // Update column sort direction indicator
            e.Column.SortDirection = _isAscending
                ? DataGridSortDirection.Ascending
                : DataGridSortDirection.Descending;

            // Clear other column indicators
            foreach (var column in DetailsDataGrid.Columns)
            {
                if (column != e.Column)
                {
                    column.SortDirection = null;
                }
            }

            Helpers.DebugLogger.Log($"[DetailsModeView] DataGrid sorted by {sortBy} ({(_isAscending ? "Asc" : "Desc")})");
        }

        private void OnColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            Helpers.DebugLogger.Log($"[DetailsModeView] Column reordered: {e.Column.Header}");
            SaveColumnSettings();
        }

        private void SortItems(string sortBy, bool ascending)
        {
            if (ViewModel?.CurrentFolder == null || ViewModel.CurrentFolder.Children.Count == 0)
                return;

            var column = ViewModel.CurrentFolder;

            // CRITICAL: Save selection before sorting
            var savedSelection = column.SelectedChild;

            // Set sorting flag to prevent PropertyChanged events
            column.IsSorting = true;

            try
            {
                // Sort folders first, then files
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

                // Update collection
                column.Children.Clear();
                foreach (var item in sortedList)
                {
                    column.Children.Add(item);
                }

                // Restore selection
                if (savedSelection != null)
                {
                    column.SelectedChild = savedSelection;
                }

                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted by {sortBy} ({(ascending ? "Ascending" : "Descending")}), {sortedList.Count} items");
            }
            finally
            {
                // Always clear sorting flag
                column.IsSorting = false;
            }
        }

        private void SaveColumnSettings()
        {
            try
            {
                // Check if DataGrid and its columns are still valid
                if (DetailsDataGrid == null || DetailsDataGrid.Columns == null || DetailsDataGrid.Columns.Count == 0)
                {
                    Helpers.DebugLogger.Log("[DetailsModeView] DataGrid not valid, skipping save");
                    return;
                }

                var composite = new ApplicationDataCompositeValue();

                // Save column widths
                for (int i = 0; i < DetailsDataGrid.Columns.Count; i++)
                {
                    var column = DetailsDataGrid.Columns[i];
                    if (column != null)
                    {
                        composite[$"Column{i}_Width"] = column.ActualWidth;
                        composite[$"Column{i}_DisplayIndex"] = column.DisplayIndex;
                    }
                }

                // Save sort settings
                composite["SortColumn"] = _currentSortBy;
                composite["SortAscending"] = _isAscending;

                _localSettings.Values["DetailsViewColumns"] = composite;
                Helpers.DebugLogger.Log("[DetailsModeView] Column settings saved");
            }
            catch (ObjectDisposedException)
            {
                // Ignore if objects are already disposed during shutdown
                Helpers.DebugLogger.Log("[DetailsModeView] Objects disposed, skipping save");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error saving column settings: {ex.Message}");
            }
        }

        private void RestoreColumnSettings()
        {
            try
            {
                if (_localSettings.Values["DetailsViewColumns"] is ApplicationDataCompositeValue composite)
                {
                    // Restore column widths
                    for (int i = 0; i < DetailsDataGrid.Columns.Count; i++)
                    {
                        var column = DetailsDataGrid.Columns[i];

                        if (composite.TryGetValue($"Column{i}_Width", out var widthObj) && widthObj is double width)
                        {
                            column.Width = new DataGridLength(width);
                        }

                        if (composite.TryGetValue($"Column{i}_DisplayIndex", out var indexObj) && indexObj is int displayIndex)
                        {
                            column.DisplayIndex = displayIndex;
                        }
                    }

                    // Restore sort settings
                    if (composite.TryGetValue("SortColumn", out var sortObj) && sortObj is string sortColumn)
                    {
                        _currentSortBy = sortColumn;
                    }

                    if (composite.TryGetValue("SortAscending", out var ascObj) && ascObj is bool ascending)
                    {
                        _isAscending = ascending;
                    }

                    // Apply restored sort
                    SortItems(_currentSortBy, _isAscending);

                    // Update visual indicators
                    var sortedColumn = DetailsDataGrid.Columns.FirstOrDefault(c => c.Tag?.ToString() == _currentSortBy);
                    if (sortedColumn != null)
                    {
                        sortedColumn.SortDirection = _isAscending
                            ? DataGridSortDirection.Ascending
                            : DataGridSortDirection.Descending;
                    }

                    Helpers.DebugLogger.Log("[DetailsModeView] Column settings restored");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error restoring column settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Focus the Details DataGrid (called from MainWindow on view switch)
        /// </summary>
        public void FocusDataGrid()
        {
            DetailsDataGrid?.Focus(FocusState.Programmatic);
        }

        // Keep old method for compatibility
        public void FocusListView() => FocusDataGrid();

        /// <summary>
        /// CRITICAL: Cleanup called from MainWindow.OnClosed BEFORE views are unloaded
        /// This prevents WinUI crash by disconnecting bindings early
        /// </summary>
        public void Cleanup()
        {
            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] Starting early cleanup...");

                if (DetailsDataGrid != null)
                {
                    // Disconnect events FIRST
                    DetailsDataGrid.Sorting -= OnDataGridSorting;
                    DetailsDataGrid.ColumnReordered -= OnColumnReordered;
                    DetailsDataGrid.DoubleTapped -= OnItemDoubleClick;
                    DetailsDataGrid.KeyDown -= OnDetailsKeyDown;

                    // Clear data bindings to prevent WinUI internal crash
                    DetailsDataGrid.ItemsSource = null;
                    DetailsDataGrid.SelectedItem = null;

                    Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] DataGrid disconnected");
                }

                // Clear ViewModel reference
                ViewModel = null;

                Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] Early cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.Cleanup] Error: {ex.Message}");
            }
        }
    }
}
