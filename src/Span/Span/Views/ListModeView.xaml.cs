using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Span.Views
{
    public sealed partial class ListModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }
        public IntPtr OwnerHwnd { get; set; }
        public bool IsRightPane { get; set; }
        public bool IsManualViewModel { get; set; }

        private ExplorerViewModel? _viewModel;

        public bool SuppressSortOnAssign { get; set; }

        /// <summary>
        /// The custom items list for List GridView: [..] + sorted children.
        /// </summary>
        private readonly ObservableCollection<FileSystemViewModel> _listItems = new();

        /// <summary>
        /// The ".." parent directory FolderViewModel, or null if at root.
        /// </summary>
        private FolderViewModel? _parentDotDotVm;

        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnExplorerPropertyChanged;
                }

                _viewModel = value;
                RootGrid.DataContext = _viewModel;

                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += OnExplorerPropertyChanged;

                    if (_isLoaded && !SuppressSortOnAssign)
                    {
                        RebuildListItems();
                    }
                }
                SuppressSortOnAssign = false;
            }
        }

        private bool _isLoaded = false;
        private bool _isCleanedUp = false;
        private bool _showSize = true;
        private bool _showDate = false;
        private double _columnWidth = 250;
        private SettingsService? _settings;
        private LocalizationService? _loc;

        // F2 rename cycling state
        private int _renameSelectionCycle = 0;
        private string? _renameTargetPath;
        private bool _justFinishedRename = false;

        public ListModeView()
        {
            this.InitializeComponent();

            // Set code-behind managed ItemsSource
            ListGridView.ItemsSource = _listItems;

            // Use AddHandler with handledEventsToo=true so Enter/Backspace/F2
            // reach our handler even when GridView internally marks them as handled.
            ListGridView.AddHandler(UIElement.KeyDownEvent,
                new Microsoft.UI.Xaml.Input.KeyEventHandler(OnListKeyDown), true);

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                _isCleanedUp = false;

                if (!IsManualViewModel)
                {
                    if (this.XamlRoot?.Content is FrameworkElement root &&
                        root.DataContext is MainViewModel mainVm)
                    {
                        ViewModel = IsRightPane ? mainVm.RightExplorer : mainVm.Explorer;
                    }
                }

                try
                {
                    _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
                    if (_settings != null)
                    {
                        ApplyCheckboxMode(_settings.ShowCheckboxes);
                        _settings.SettingChanged += OnSettingChanged;

                        // Restore saved List settings
                        LoadListSettings();
                    }
                }
                catch { }

                try
                {
                    _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                    LocalizeUI();
                    if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                }
                catch { }

                // Build initial items with ".." prepended
                RebuildListItems();
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            if (_isCleanedUp) return;
            PerformCleanup();
        }

        /// <summary>
        /// React to ExplorerViewModel property changes (CurrentPath, CurrentFolder, etc.).
        /// Rebuild the List items when the folder changes.
        /// </summary>
        private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExplorerViewModel.CurrentFolder) ||
                e.PropertyName == nameof(ExplorerViewModel.CurrentItems) ||
                e.PropertyName == nameof(ExplorerViewModel.CurrentPath))
            {
                DispatcherQueue.TryEnqueue(() => RebuildListItems());
            }
        }

        #region Localization

        private void LocalizeUI()
        {
            if (_loc == null) return;
            SizeToggle.Content = _loc.Get("Size");
            DateToggle.Content = _loc.Get("Date");
        }

        #endregion

        #region Settings Save/Restore

        /// <summary>
        /// Load saved List preferences and apply to UI controls.
        /// </summary>
        private void LoadListSettings()
        {
            if (_settings == null) return;

            _showSize = _settings.ListShowSize;
            _showDate = _settings.ListShowDate;
            _columnWidth = _settings.ListColumnWidth;

            // Apply to UI controls
            SizeToggle.IsChecked = _showSize;
            DateToggle.IsChecked = _showDate;
            ColumnWidthSlider.Value = _columnWidth;
            ColumnWidthLabel.Text = $"{(int)_columnWidth}px";

            // Apply column width to WrapGrid if already materialized
            if (ListGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = _columnWidth;
            }
        }

        /// <summary>
        /// Save current List settings to persistent storage.
        /// </summary>
        private void SaveListSettings()
        {
            if (_settings == null) return;

            _settings.ListShowSize = _showSize;
            _settings.ListShowDate = _showDate;
            _settings.ListColumnWidth = (int)_columnWidth;
        }

        #endregion

        #region ".." Parent Item + Sorting

        /// <summary>
        /// Public entry point for external sort trigger.
        /// </summary>
        public void RebuildListItemsPublic() => RebuildListItems();

        /// <summary>
        /// Rebuild the List items list: [..] + directories (sorted) + files (sorted).
        /// </summary>
        private void RebuildListItems()
        {
            if (ViewModel?.CurrentFolder == null)
            {
                _listItems.Clear();
                _parentDotDotVm = null;
                return;
            }

            var folder = ViewModel.CurrentFolder;
            var savedSelection = folder.SelectedChild;
            folder.IsSorting = true;

            try
            {
                _listItems.Clear();

                // 1. Create ".." entry if not at root
                _parentDotDotVm = CreateParentDotDotVm(folder.Path);
                if (_parentDotDotVm != null)
                {
                    _listItems.Add(_parentDotDotVm);
                }

                // 2. FolderViewModel.Children은 이미 정렬됨 (PopulateChildren/SortChildren)
                var sorted = folder.Children.ToList();

                foreach (var item in sorted)
                    _listItems.Add(item);

                // 3. Restore selection or select first item for keyboard nav
                if (savedSelection != null && savedSelection.Name != ".." && _listItems.Contains(savedSelection))
                {
                    ListGridView.SelectedItem = savedSelection;
                }
                else if (_listItems.Count > 0)
                {
                    // Auto-select first item so arrow keys start from a known position
                    ListGridView.SelectedItem = _listItems[0];
                }
            }
            finally
            {
                folder.IsSorting = false;
            }
        }

        // ── Group By ──
        private string _currentGroupBy = "None";

        public void ApplyGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;
            RebuildGroupedListItems();
        }

        private void RebuildGroupedListItems()
        {
            if (ViewModel?.CurrentFolder == null) return;

            if (_currentGroupBy == "None" || string.IsNullOrEmpty(_currentGroupBy))
            {
                // 그룹 해제 — 일반 리빌드
                ListGridView.ItemsSource = _listItems;
                RebuildListItems();
                return;
            }

            // 그룹 모드: ".." 제외하고 그룹핑
            var folder = ViewModel.CurrentFolder;
            var items = folder.Children.ToList();

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
            ListGridView.ItemsSource = cvs.View;
        }

        /// <summary>
        /// Create a ".." FolderViewModel pointing to the parent directory.
        /// Returns null if already at root (drive root or remote root).
        /// </summary>
        private static FolderViewModel? CreateParentDotDotVm(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath)) return null;

            try
            {
                string? parentPath;

                if (FileSystemRouter.IsRemotePath(currentPath))
                {
                    var remotePath = FileSystemRouter.ExtractRemotePath(currentPath);
                    if (remotePath == "/" || string.IsNullOrEmpty(remotePath))
                        return null; // Remote root

                    var prefix = FileSystemRouter.GetUriPrefix(currentPath);
                    var parentRemote = remotePath.TrimEnd('/');
                    var lastSlash = parentRemote.LastIndexOf('/');
                    if (lastSlash <= 0) parentRemote = "/";
                    else parentRemote = parentRemote.Substring(0, lastSlash);

                    parentPath = prefix + parentRemote;
                }
                else
                {
                    parentPath = System.IO.Path.GetDirectoryName(currentPath);
                    if (string.IsNullOrEmpty(parentPath)) return null; // Drive root (e.g., C:\)
                }

                var parentItem = new FolderItem
                {
                    Name = "..",
                    Path = parentPath
                };

                var fileService = App.Current.Services.GetService(typeof(FileSystemService)) as FileSystemService;
                if (fileService == null) return null;

                return new FolderViewModel(parentItem, fileService);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a FileSystemViewModel is the ".." parent entry.
        /// </summary>
        private bool IsParentDotDot(FileSystemViewModel? item)
        {
            return item != null && item == _parentDotDotVm;
        }

        #endregion

        #region Selection

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is not GridView gridView) return;

            // Filter out ".." from selection sync — prevents file operations on parent dir
            var realItems = gridView.SelectedItems
                .OfType<FileSystemViewModel>()
                .Where(x => !IsParentDotDot(x))
                .Cast<object>()
                .ToList();

            ViewModel.CurrentFolder.SyncSelectedItems(realItems);

            // Also set SelectedChild for single selection (excluding "..")
            if (gridView.SelectedItems.Count == 1)
            {
                var single = gridView.SelectedItems[0] as FileSystemViewModel;
                if (single != null && !IsParentDotDot(single))
                {
                    ViewModel.CurrentFolder.SelectedChild = single;
                }
            }
        }

        private void ApplyCheckboxMode(bool showCheckboxes)
        {
            if (ListGridView == null) return;
            ListGridView.SelectionMode = showCheckboxes
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

        #endregion

        #region Item Interaction

        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Filter out ".." before delegating to shared helper
            var filtered = e.Items.OfType<FileSystemViewModel>()
                .Where(x => IsParentDotDot(x)).ToList();
            foreach (var item in filtered) e.Items.Remove(item);

            if (!Helpers.ViewDragDropHelper.SetupDragData(e, IsRightPane))
                e.Cancel = true;
        }

        private async void OnItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (sender is Grid grid && ContextMenuService != null && ContextMenuHost != null)
            {
                // ".." → show empty area menu (same as right-clicking background)
                if (grid.DataContext is FolderViewModel folder && IsParentDotDot(folder))
                {
                    var folderPath = ViewModel?.CurrentFolder?.Path;
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        var emptyFlyout = ContextMenuService.BuildEmptyAreaMenu(folderPath, ContextMenuHost);
                        emptyFlyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                        {
                            Position = e.GetPosition(grid)
                        });
                    }
                    e.Handled = true;
                    return;
                }

                e.Handled = true; // Prevent bubbling to empty area handler during await

                MenuFlyout? flyout = null;

                if (grid.DataContext is FolderViewModel realFolder)
                    flyout = await ContextMenuService.BuildFolderMenuAsync(realFolder, ContextMenuHost);
                else if (grid.DataContext is FileViewModel file)
                    flyout = await ContextMenuService.BuildFileMenuAsync(file, ContextMenuHost);

                if (flyout != null)
                {
                    flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = e.GetPosition(grid)
                    });
                }
            }
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (_justFinishedRename) { _justFinishedRename = false; return; }

            var selected = GetSelectedItem();
            if (selected == null) return;

            // ".." → navigate up
            if (IsParentDotDot(selected))
            {
                ViewModel?.NavigateUp();
                return;
            }

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                    shellService.OpenFile(file.Path);
                }
                catch { }
            }
        }

        #endregion

        #region Keyboard Navigation

        private void OnListKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Skip if the event originates from the rename TextBox — it has its own handlers
            if (e.OriginalSource is TextBox) return;

            var selected = GetSelectedItem();
            if (selected != null && !IsParentDotDot(selected) && selected.IsRenaming) return;

            if (Helpers.ViewItemHelper.HasModifierKey()) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    HandleEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.F2:
                    if (selected != null && IsParentDotDot(selected))
                    {
                        e.Handled = true; // Block rename on ".."
                    }
                    else
                    {
                        HandleRename();
                        e.Handled = true; // Prevent global handler from also handling
                    }
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Block Delete on ".." item
                    if (selected != null && IsParentDotDot(selected))
                    {
                        e.Handled = true;
                    }
                    // Otherwise let global handler handle
                    break;
            }
        }

        private void HandleEnter()
        {
            if (_justFinishedRename) { _justFinishedRename = false; return; }

            var selected = GetSelectedItem();
            if (selected == null) return;

            // ".." → navigate up
            if (IsParentDotDot(selected))
            {
                ViewModel?.NavigateUp();
                return;
            }

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                    shellService.OpenFile(file.Path);
                }
                catch { }
            }
        }

        /// <summary>
        /// Get the currently selected item from the GridView.
        /// </summary>
        private FileSystemViewModel? GetSelectedItem()
        {
            return ListGridView?.SelectedItem as FileSystemViewModel;
        }

        #endregion

        #region F2 Inline Rename

        /// <summary>
        /// Start inline rename for the selected item (F2).
        /// Handles F2 cycling: name-only → all → extension-only.
        /// </summary>
        private void HandleRename()
        {
            var selected = GetSelectedItem();
            if (selected == null || IsParentDotDot(selected)) return;

            var itemPath = selected.Path;

            // F2 cycling: if already renaming the same item, advance selection cycle
            if (selected.IsRenaming && itemPath == _renameTargetPath)
            {
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    FocusListRenameTextBox(selected);
                });
                return;
            }

            // First F2 press: start rename
            _renameSelectionCycle = 0;
            _renameTargetPath = itemPath;
            selected.BeginRename();

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                FocusListRenameTextBox(selected);
            });
        }

        /// <summary>
        /// Find and focus the rename TextBox for the given item in the List GridView.
        /// </summary>
        private void FocusListRenameTextBox(FileSystemViewModel item)
        {
            int idx = _listItems.IndexOf(item);
            if (idx < 0) return;

            var container = ListGridView.ContainerFromIndex(idx) as UIElement;
            if (container == null)
            {
                // Virtualized — scroll into view and retry
                ListGridView.ScrollIntoView(item);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var retryContainer = ListGridView.ContainerFromIndex(idx) as UIElement;
                    if (retryContainer != null)
                    {
                        var tb = FindChild<TextBox>(retryContainer as DependencyObject);
                        if (tb != null) ApplyRenameSelection(tb, item is FolderViewModel);
                    }
                });
                return;
            }

            var textBox = FindChild<TextBox>(container as DependencyObject);
            if (textBox != null)
            {
                ApplyRenameSelection(textBox, item is FolderViewModel);
            }
        }

        /// <summary>
        /// Apply F2 cycling selection to rename TextBox.
        /// </summary>
        private void ApplyRenameSelection(TextBox textBox, bool isFolder)
        {
            textBox.Focus(FocusState.Keyboard);

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (!isFolder && !string.IsNullOrEmpty(textBox.Text))
                {
                    int dotIndex = textBox.Text.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
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
                // Re-focus the GridView item
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
                HandleRename();
                e.Handled = true;
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null || !vm.IsRenaming) return;

            vm.CommitRename();
            _renameTargetPath = null;
        }

        /// <summary>
        /// Focus the currently selected GridView item container after rename.
        /// </summary>
        private void FocusSelectedGridViewItem()
        {
            var selected = GetSelectedItem();
            if (selected == null) return;

            int idx = _listItems.IndexOf(selected);
            if (idx < 0) return;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (ListGridView.ContainerFromIndex(idx) is GridViewItem container)
                {
                    container.Focus(FocusState.Programmatic);
                }
            });
        }

        /// <summary>
        /// Recursive visual tree search for child of type T.
        /// </summary>
        private static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region Toolbar Controls

        private void OnSizeToggleClick(object sender, RoutedEventArgs e)
        {
            _showSize = SizeToggle.IsChecked == true;
            UpdateColumnVisibility();
            SaveListSettings();
        }

        private void OnDateToggleClick(object sender, RoutedEventArgs e)
        {
            _showDate = DateToggle.IsChecked == true;
            UpdateColumnVisibility();
            SaveListSettings();
        }

        private void OnColumnWidthChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _columnWidth = e.NewValue;
            ColumnWidthLabel.Text = $"{(int)_columnWidth}px";

            // Update ItemsWrapGrid item width
            if (ListGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = _columnWidth;
            }

            SaveListSettings();
        }

        private void UpdateColumnVisibility()
        {
            if (ListGridView?.ItemsPanelRoot == null) return;

            for (int i = 0; i < ListGridView.Items.Count; i++)
            {
                if (ListGridView.ContainerFromIndex(i) is GridViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    ApplyColumnVisibility(grid);
                }
            }
        }

        private void ApplyColumnVisibility(Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is TextBlock tb)
                {
                    var col = Grid.GetColumn(tb);
                    if (col == 2) // Size
                        tb.Visibility = _showSize ? Visibility.Visible : Visibility.Collapsed;
                    else if (col == 3) // Date
                        tb.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        #endregion

        #region Focus Management

        public void FocusGridView()
        {
            ListGridView?.Focus(FocusState.Programmatic);
        }

        private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ListGridView?.Focus(FocusState.Programmatic);
        }

        private void OnEmptyAreaRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.Handled) return;
            if (_settings != null && !_settings.ShowContextMenu) return;
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
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnExplorerPropertyChanged;
                }

                if (_settings != null)
                {
                    _settings.SettingChanged -= OnSettingChanged;
                    _settings = null;
                }

                if (ListGridView != null)
                {
                    ListGridView.DoubleTapped -= OnItemDoubleClick;
                    ListGridView.RemoveHandler(UIElement.KeyDownEvent,
                        new Microsoft.UI.Xaml.Input.KeyEventHandler(OnListKeyDown));
                    ListGridView.ItemsSource = null;
                    ListGridView.SelectedItem = null;
                }

                _parentDotDotVm = null;
                _listItems.Clear();
                _viewModel = null;
                RootGrid.DataContext = null;

                Helpers.DebugLogger.Log("[ListModeView] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ListModeView] Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}
