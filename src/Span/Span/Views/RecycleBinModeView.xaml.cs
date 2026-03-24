using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;
using Span.Services;

namespace Span.Views;

public sealed partial class RecycleBinModeView : UserControl
{
    private readonly LocalizationService _loc;
    private SettingsService? _settings;
    private List<RecycleBinItem> _allItems = new();
    private readonly ObservableCollection<RecycleBinItem> _displayItems = new();
    private CancellationTokenSource? _loadCts;

    // Sort state
    private string _currentSortBy = "DateDeleted";
    private bool _isAscending = false; // newest first by default

    // Column widths (synced from header ColumnDefinitions)
    private double _origLocationColumnWidth = 200;
    private double _dateDeletedColumnWidth = 200;
    private double _sizeColumnWidth = 100;
    private double _typeColumnWidth = 150;

    // Cell total widths (including splitter gaps) — Gotcha #18
    private double _origLocationCellTotalWidth = 200;
    private double _dateDeletedCellTotalWidth = 200;
    private double _sizeCellTotalWidth = 100;
    private double _typeCellTotalWidth = 150;

    // ColumnDefinition callback tokens
    private long _origLocationCallbackToken;
    private long _dateDeletedCallbackToken;
    private long _sizeCallbackToken;
    private long _typeCallbackToken;
    private long _splitter1CallbackToken;
    private long _splitter2CallbackToken;
    private long _splitter3CallbackToken;
    private long _splitter4CallbackToken;

    private bool _columnWidthUpdatePending;
    private bool _isLoaded;
    private bool _isSyncingSelection;
    private Helpers.RubberBandSelectionHelper? _rubberBandHelper;

    public event EventHandler<List<RecycleBinItem>>? RestoreRequested;
    public event EventHandler<List<RecycleBinItem>>? DeletePermanentlyRequested;
    public event EventHandler? EmptyRequested;
    public event EventHandler<RecycleBinItem>? OpenOriginalLocationRequested;
    public event EventHandler<RecycleBinItem>? PropertiesRequested;
    public event EventHandler? RefreshCompleted;
    public event EventHandler<(int ItemCount, int SelectedCount, long TotalSize, long SelectedSize)>? StatusChanged;

    public RecycleBinModeView()
    {
        this.InitializeComponent();
        _loc = App.Current.Services.GetRequiredService<LocalizationService>();
        ApplyLocalization();

        RecycleBinListView.ItemsSource = _displayItems;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        // Apply ShowCheckboxes setting (same pattern as DetailsModeView)
        try
        {
            _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
            if (_settings != null)
            {
                ApplyCheckboxMode(_settings.ShowCheckboxes);
                _settings.SettingChanged += OnSettingChanged;
            }
        }
        catch (Exception ex) { DebugLogger.Log($"[RecycleBinView] Settings init error: {ex.Message}"); }

        // Register ColumnDefinition width change callbacks (Gotcha #18)
        _origLocationCallbackToken = OrigLocationColumnDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _dateDeletedCallbackToken = DateDeletedColumnDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _sizeCallbackToken = SizeColumnDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _typeCallbackToken = TypeColumnDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);

        _splitter1CallbackToken = Splitter1ColDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _splitter2CallbackToken = Splitter2ColDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _splitter3CallbackToken = Splitter3ColDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);
        _splitter4CallbackToken = Splitter4ColDef.RegisterPropertyChangedCallback(
            ColumnDefinition.WidthProperty, OnColumnWidthChanged);

        // Initial sync on first HeaderGrid size
        void OnHeaderFirstSize(object s, SizeChangedEventArgs ev)
        {
            HeaderGrid.SizeChanged -= OnHeaderFirstSize;
            OnColumnWidthChanged(this, ColumnDefinition.WidthProperty);
        }
        HeaderGrid.SizeChanged += OnHeaderFirstSize;

        UpdateSortIndicators();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;

        _rubberBandHelper?.Detach();
        _rubberBandHelper = null;

        if (_settings != null)
            _settings.SettingChanged -= OnSettingChanged;

        OrigLocationColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _origLocationCallbackToken);
        DateDeletedColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _dateDeletedCallbackToken);
        SizeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _sizeCallbackToken);
        TypeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _typeCallbackToken);

        Splitter1ColDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _splitter1CallbackToken);
        Splitter2ColDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _splitter2CallbackToken);
        Splitter3ColDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _splitter3CallbackToken);
        Splitter4ColDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _splitter4CallbackToken);
    }

    private void ApplyCheckboxMode(bool showCheckboxes)
    {
        if (RecycleBinListView == null) return;
        RecycleBinListView.SelectionMode = showCheckboxes
            ? ListViewSelectionMode.Multiple
            : ListViewSelectionMode.Extended;
    }

    private void OnSettingChanged(string key, object? value)
    {
        if (key == "ShowCheckboxes" && value is bool show)
        {
            Helpers.DispatcherHelper.SafeEnqueue(DispatcherQueue, () => ApplyCheckboxMode(show));
        }
    }

    private void ApplyLocalization()
    {
        EmptyStateText.Text = _loc.Get("RecycleBin_EmptyState");
    }

    private void LocalizeHeaders()
    {
        NameHeaderButton.Content = _loc.Get("Name");
        OrigLocationHeaderButton.Content = _loc.Get("RecycleBin_OriginalLocation");
        DateDeletedHeaderButton.Content = _loc.Get("RecycleBin_DateDeleted");
        SizeHeaderButton.Content = _loc.Get("Size");
        TypeHeaderButton.Content = _loc.Get("Type");
        UpdateSortIndicators();
    }

    #region Column Width Synchronization (Gotcha #17, #18)

    private void OnColumnWidthChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (OrigLocationColumnDef.ActualWidth > 0) _origLocationColumnWidth = OrigLocationColumnDef.ActualWidth;
        if (DateDeletedColumnDef.ActualWidth > 0) _dateDeletedColumnWidth = DateDeletedColumnDef.ActualWidth;
        if (SizeColumnDef.ActualWidth > 0) _sizeColumnWidth = SizeColumnDef.ActualWidth;
        if (TypeColumnDef.ActualWidth > 0) _typeColumnWidth = TypeColumnDef.ActualWidth;

        RecalcCellTotalWidths();

        if (!_columnWidthUpdatePending)
        {
            _columnWidthUpdatePending = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                _columnWidthUpdatePending = false;
                if (!_isLoaded) return;
                UpdateAllVisibleContainerWidths();
            });
        }
    }

    private void RecalcCellTotalWidths()
    {
        try
        {
            var colDefs = HeaderGrid.ColumnDefinitions;
            if (colDefs.Count < 10) return;

            // Name end X = col0 (icon 32) + col1 (name *)
            double nameEndX = colDefs[0].ActualWidth + colDefs[1].ActualWidth;
            if (nameEndX <= 0) return;

            // OrigLocation (col 3): starts after splitter1 (col 2)
            double origLocEndX = nameEndX;
            for (int i = 2; i <= 3; i++) origLocEndX += colDefs[i].ActualWidth;
            _origLocationCellTotalWidth = origLocEndX - nameEndX;

            // DateDeleted (col 5)
            double dateEndX = origLocEndX;
            for (int i = 4; i <= 5; i++) dateEndX += colDefs[i].ActualWidth;
            _dateDeletedCellTotalWidth = dateEndX - origLocEndX;

            // Size (col 7)
            double sizeEndX = dateEndX;
            for (int i = 6; i <= 7; i++) sizeEndX += colDefs[i].ActualWidth;
            _sizeCellTotalWidth = sizeEndX - dateEndX;

            // Type (col 9)
            double typeEndX = sizeEndX;
            for (int i = 8; i <= 9; i++) typeEndX += colDefs[i].ActualWidth;
            _typeCellTotalWidth = typeEndX - sizeEndX;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[RecycleBinView] RecalcCellTotalWidths error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply column widths to a single item Grid. Only sets Border.Width (safe in measure pass — Gotcha #17).
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
                    case 2: border.Width = _origLocationCellTotalWidth; break;
                    case 3: border.Width = _dateDeletedCellTotalWidth; break;
                    case 4: border.Width = _sizeCellTotalWidth; break;
                    case 5: border.Width = _typeCellTotalWidth; break;
                }
            }
        }
    }

    private void UpdateAllVisibleContainerWidths()
    {
        var panel = RecycleBinListView?.ItemsPanelRoot;
        if (panel == null) return;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
        {
            if (VisualTreeHelper.GetChild(panel, i) is ListViewItem container &&
                container.ContentTemplateRoot is Grid grid)
            {
                ApplyCellWidths(grid);
            }
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;

        if (args.ItemContainer?.ContentTemplateRoot is Grid grid)
        {
            ApplyCellWidths(grid);

            // Populate dynamic fields not suitable for x:Bind
            if (args.Item is RecycleBinItem item)
            {
                // DateDeleted — formatted
                var dateCell = FindNamedChild<Border>(grid, "DateDeletedCell");
                if (dateCell?.Child is TextBlock dateTb)
                {
                    dateTb.Text = item.DateDeleted != default
                        ? item.DateDeleted.ToString("yyyy-MM-dd HH:mm")
                        : "";
                }

                // Size — formatted
                var sizeCell = FindNamedChild<Border>(grid, "SizeCell");
                if (sizeCell?.Child is TextBlock sizeTb)
                {
                    sizeTb.Text = item.IsFolder ? "" : FormatFileSize(item.Size);
                }
            }
        }
    }

    private static T? FindNamedChild<T>(Grid grid, string name) where T : FrameworkElement
    {
        foreach (var child in grid.Children)
        {
            if (child is T fe && fe.Name == name) return fe;
        }
        return null;
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            > 0 => $"{bytes} B",
            _ => ""
        };
    }

    #endregion

    #region Data Loading

    public async Task LoadItemsAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            var service = App.Current.Services.GetRequiredService<RecycleBinService>();
            _allItems = await service.GetItemsAsync(ct);

            ct.ThrowIfCancellationRequested();

            SortAndDisplay();
            EmptyState.Visibility = _allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatusText();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DebugLogger.Log($"[RecycleBinView] Load error: {ex.Message}");
        }
    }

    private void SortAndDisplay()
    {
        IEnumerable<RecycleBinItem> sorted = _currentSortBy switch
        {
            "Name" => _isAscending
                ? _allItems.OrderBy(x => x.Name, NaturalStringComparer.Instance)
                : _allItems.OrderByDescending(x => x.Name, NaturalStringComparer.Instance),
            "OriginalLocation" => _isAscending
                ? _allItems.OrderBy(x => x.OriginalLocation, StringComparer.OrdinalIgnoreCase)
                : _allItems.OrderByDescending(x => x.OriginalLocation, StringComparer.OrdinalIgnoreCase),
            "DateDeleted" => _isAscending
                ? _allItems.OrderBy(x => x.DateDeleted)
                : _allItems.OrderByDescending(x => x.DateDeleted),
            "Size" => _isAscending
                ? _allItems.OrderBy(x => x.Size)
                : _allItems.OrderByDescending(x => x.Size),
            "Type" => _isAscending
                ? _allItems.OrderBy(x => x.ItemType, StringComparer.OrdinalIgnoreCase)
                : _allItems.OrderByDescending(x => x.ItemType, StringComparer.OrdinalIgnoreCase),
            _ => _allItems.OrderByDescending(x => x.DateDeleted)
        };

        _displayItems.Clear();
        foreach (var item in sorted)
            _displayItems.Add(item);
    }

    /// <summary>
    /// 검색 필터링. null/빈 문자열이면 전체 표시.
    /// 지원: 이름 포함 검색, *.ext 와일드카드, ext:.ext 구문
    /// </summary>
    public void FilterItems(string? query)
    {
        _displayItems.Clear();
        List<RecycleBinItem> source;

        if (string.IsNullOrWhiteSpace(query))
        {
            source = _allItems;
        }
        else
        {
            var q = query.Trim();
            // ext:.png 구문
            if (q.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var ext = q[4..].Trim().TrimStart('.');
                source = _allItems.Where(x =>
                    x.Name.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            // *.png 와일드카드
            else if (q.StartsWith("*."))
            {
                var ext = q[1..]; // ".png"
                source = _allItems.Where(x =>
                    x.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            // *keyword* 와일드카드
            else if (q.StartsWith("*") && q.EndsWith("*") && q.Length > 2)
            {
                var keyword = q[1..^1];
                source = _allItems.Where(x =>
                    x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            // 일반 텍스트: 이름/원래위치/종류에서 포함 검색
            else
            {
                source = _allItems.Where(x =>
                    x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || x.OriginalLocation.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || x.ItemType.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        foreach (var item in source)
            _displayItems.Add(item);

        StatusChanged?.Invoke(this, (source.Count, 0, source.Sum(i => i.Size), 0));
    }

    private void UpdateStatusText()
    {
        long totalSize = _allItems.Sum(i => i.Size);
        StatusChanged?.Invoke(this, (_allItems.Count, 0, totalSize, 0));
    }

    #endregion

    #region Sort

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sortBy)
        {
            if (_currentSortBy == sortBy)
                _isAscending = !_isAscending;
            else
            {
                _currentSortBy = sortBy;
                _isAscending = true;
            }

            SortAndDisplay();
            UpdateSortIndicators();
        }
    }

    private void UpdateSortIndicators()
    {
        var headers = new[] { NameHeaderButton, OrigLocationHeaderButton, DateDeletedHeaderButton, SizeHeaderButton, TypeHeaderButton };
        string[] tags = { "Name", "OriginalLocation", "DateDeleted", "Size", "Type" };
        string[] defaultLabels = {
            _loc.Get("Name"),
            _loc.Get("RecycleBin_OriginalLocation"),
            _loc.Get("RecycleBin_DateDeleted"),
            _loc.Get("Size"),
            _loc.Get("Type")
        };

        for (int i = 0; i < headers.Length; i++)
        {
            if (headers[i] == null) continue;
            headers[i].Content = _currentSortBy == tags[i]
                ? $"{defaultLabels[i]} {(_isAscending ? "\u25B2" : "\u25BC")}"
                : defaultLabels[i];
        }
    }

    #endregion

    #region Selection & Events

    public List<RecycleBinItem> GetSelectedItems()
    {
        return RecycleBinListView.SelectedItems
            .OfType<RecycleBinItem>()
            .ToList();
    }

    public void SelectAll()
    {
        RecycleBinListView.SelectAll();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        int selCount = RecycleBinListView.SelectedItems.Count;
        bool hasSelection = selCount > 0;
        // Notify MainWindow status bar
        long totalSize = _allItems.Sum(i => i.Size);
        long selectedSize = hasSelection
            ? RecycleBinListView.SelectedItems.OfType<RecycleBinItem>().Sum(i => i.Size)
            : 0;
        StatusChanged?.Invoke(this, (_allItems.Count, selCount, totalSize, selectedSize));
    }

    /// <summary>
    /// Empty area click — deselect all and focus ListView (same pattern as DetailsModeView.OnRootTapped).
    /// </summary>
    private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        RecycleBinListView?.Focus(FocusState.Programmatic);
    }

    // ── Unified Bar 연동용 public API ──
    public bool HasSelection => RecycleBinListView.SelectedItems.Count > 0;
    public void InvokeRestore() => OnRestoreClicked(this, new RoutedEventArgs());
    public void InvokeDeletePermanently() => OnDeletePermanentlyClicked(this, new RoutedEventArgs());
    public void InvokeEmpty() => OnEmptyRecycleBinClicked(this, new RoutedEventArgs());
    public void InvokeRefresh() => OnRefreshClicked(this, new RoutedEventArgs());

    private void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Count > 0)
            RestoreRequested?.Invoke(this, selected);
    }

    private void OnDeletePermanentlyClicked(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Count > 0)
            DeletePermanentlyRequested?.Invoke(this, selected);
    }

    private void OnEmptyRecycleBinClicked(object sender, RoutedEventArgs e)
    {
        EmptyRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadItemsAsync();
        RefreshCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Count == 1)
            PropertiesRequested?.Invoke(this, selected[0]);
    }

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe)
        {
            var item = fe.DataContext as RecycleBinItem;
            if (item != null && !RecycleBinListView.SelectedItems.Contains(item))
            {
                RecycleBinListView.SelectedItem = item;
            }
        }

        var selected = GetSelectedItems();
        if (selected.Count == 0)
        {
            ShowEmptyAreaContextMenu(e);
            return;
        }

        ShowItemContextMenu(selected, e);
    }

    private void ShowItemContextMenu(List<RecycleBinItem> selectedItems, RightTappedRoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var restoreItem = new MenuFlyoutItem
        {
            Text = _loc.Get("RecycleBin_Restore"),
            Icon = new FontIcon { Glyph = "\uE845" }
        };
        restoreItem.Click += (_, _) => RestoreRequested?.Invoke(this, selectedItems);
        menu.Items.Add(restoreItem);

        var deleteItem = new MenuFlyoutItem
        {
            Text = _loc.Get("RecycleBin_DeletePermanently"),
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += (_, _) => DeletePermanentlyRequested?.Invoke(this, selectedItems);
        menu.Items.Add(deleteItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        if (selectedItems.Count == 1)
        {
            var openLocationItem = new MenuFlyoutItem
            {
                Text = _loc.Get("RecycleBin_OpenOriginalLocation"),
                Icon = new FontIcon { Glyph = "\uED25" }
            };
            openLocationItem.Click += (_, _) => OpenOriginalLocationRequested?.Invoke(this, selectedItems[0]);
            menu.Items.Add(openLocationItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var propertiesItem = new MenuFlyoutItem
            {
                Text = _loc.Get("Properties"),
                Icon = new FontIcon { Glyph = "\uE946" }
            };
            propertiesItem.Click += (_, _) => PropertiesRequested?.Invoke(this, selectedItems[0]);
            menu.Items.Add(propertiesItem);
        }

        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
    }

    private void ShowEmptyAreaContextMenu(RightTappedRoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var emptyItem = new MenuFlyoutItem
        {
            Text = _loc.Get("RecycleBin_Empty"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            IsEnabled = _allItems.Count > 0
        };
        emptyItem.Click += (_, _) => EmptyRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(emptyItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var refreshItem = new MenuFlyoutItem
        {
            Text = _loc.Get("Refresh"),
            Icon = new FontIcon { Glyph = "\uE72C" }
        };
        refreshItem.Click += (_, _) => _ = LoadItemsAsync();
        menu.Items.Add(refreshItem);

        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
    }

    #endregion

    #region Rubber Band Selection

    private void OnListViewWrapperLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid || _rubberBandHelper != null) return;

        _rubberBandHelper = new Helpers.RubberBandSelectionHelper(
            grid,
            RecycleBinListView,
            () => _isSyncingSelection,
            val => _isSyncingSelection = val,
            afterSyncCallback: () => OnSelectionChanged(RecycleBinListView, null!));
    }

    private void OnListViewWrapperUnloaded(object sender, RoutedEventArgs e)
    {
        _rubberBandHelper?.Detach();
        _rubberBandHelper = null;
    }

    #endregion
}
