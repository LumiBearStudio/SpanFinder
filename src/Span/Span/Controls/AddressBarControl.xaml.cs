using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections;
using System.Linq;

namespace Span.Controls
{
    /// <summary>
    /// Reusable address bar control with breadcrumb display and inline edit mode.
    /// Used identically in single-pane, left-pane, and right-pane address bars.
    /// </summary>
    public sealed partial class AddressBarControl : UserControl
    {
        private bool _isEditMode;
        private int _scaleLevel;

        public AddressBarControl()
        {
            this.InitializeComponent();
            BreadcrumbRepeater.ElementPrepared += OnBreadcrumbElementPrepared;
            BreadcrumbRepeater.ElementClearing += OnBreadcrumbElementClearing;
        }

        /// <summary>
        /// 현재 폰트 스케일 레벨. 변경 시 기존 브레드크럼 element에 즉시 적용.
        /// ElementPrepared에서 새/재활용 element에도 자동 적용.
        /// </summary>
        public int ScaleLevel
        {
            get => _scaleLevel;
            set
            {
                _scaleLevel = value;
                ApplyScaleToBreadcrumbs();
            }
        }

        /// <summary>
        /// ItemsRepeater가 element를 준비(새 생성 또는 재활용)할 때 호출.
        /// 재활용된 element는 ConditionalWeakTable에 기존 baseline이 남아있어 정확히 재적용됨.
        /// 새 element는 XAML 기본 FontSize가 baseline으로 저장됨.
        /// ClearBaseline 불필요: 재활용 시 동일 DependencyObject이므로 기존 baseline 유지.
        /// </summary>
        private void OnBreadcrumbElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                MainWindow.ApplyAbsoluteScaleToTree(args.Element, _scaleLevel, 8, 20);
            });
        }

        /// <summary>
        /// ItemsRepeater가 element를 recycle pool로 반환할 때 호출.
        /// ConditionalWeakTable은 WeakReference이므로 GC 시 자동 정리됨.
        /// 재활용 시 동일 인스턴스이므로 baseline이 보존되어 정확한 스케일링 가능.
        /// </summary>
        private void OnBreadcrumbElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
        {
            // no-op: baseline을 명시적으로 제거하면 이중 적용 버그 발생.
            // ConditionalWeakTable이 GC 시 자동 정리하므로 수동 제거 불필요.
        }

        /// <summary>
        /// 현재 BreadcrumbRepeater의 모든 visible element에 스케일을 재적용.
        /// Global traversal(ApplyIconFontScaleToGlobalUI)은 AddressBarControl을 skip하므로
        /// 이 메서드가 AddressBar의 유일한 스케일 적용 경로.
        /// </summary>
        private void ApplyScaleToBreadcrumbs()
        {
            // ItemsRepeater.TryGetElement()로 실제 realized element만 순회
            if (BreadcrumbRepeater.ItemsSourceView != null)
            {
                for (int i = 0; i < BreadcrumbRepeater.ItemsSourceView.Count; i++)
                {
                    var element = BreadcrumbRepeater.TryGetElement(i);
                    if (element != null)
                        MainWindow.ApplyAbsoluteScaleToTree(element, _scaleLevel, 8, 20);
                }
            }

            // AutoSuggestBox와 OverflowIndicator도 스케일 적용
            MainWindow.ApplyAbsoluteScaleToTree(AutoSuggest, _scaleLevel, 8, 20, 10, 16);
            MainWindow.ApplyAbsoluteScaleToTree(OverflowIndicator, _scaleLevel, 8, 20);
        }

        #region Dependency Properties

        public static readonly DependencyProperty PathSegmentsProperty =
            DependencyProperty.Register(nameof(PathSegments), typeof(IEnumerable),
                typeof(AddressBarControl), new PropertyMetadata(null, OnPathSegmentsChanged));

        public IEnumerable PathSegments
        {
            get => (IEnumerable)GetValue(PathSegmentsProperty);
            set => SetValue(PathSegmentsProperty, value);
        }

        private static void OnPathSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AddressBarControl control)
                control.BreadcrumbRepeater.ItemsSource = e.NewValue as IEnumerable;
        }

        public static readonly DependencyProperty CurrentPathProperty =
            DependencyProperty.Register(nameof(CurrentPath), typeof(string),
                typeof(AddressBarControl), new PropertyMetadata(string.Empty));

        public string CurrentPath
        {
            get => (string)GetValue(CurrentPathProperty);
            set => SetValue(CurrentPathProperty, value);
        }

        /// <summary>
        /// Font size for breadcrumb segments. Defaults to 11 for split pane, 12 for single pane.
        /// </summary>
        public static readonly DependencyProperty BreadcrumbFontSizeProperty =
            DependencyProperty.Register(nameof(BreadcrumbFontSize), typeof(double),
                typeof(AddressBarControl), new PropertyMetadata(11.0));

        public double BreadcrumbFontSize
        {
            get => (double)GetValue(BreadcrumbFontSizeProperty);
            set => SetValue(BreadcrumbFontSizeProperty, value);
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when user submits a path via Enter or suggestion selection.
        /// </summary>
        public event EventHandler<string>? PathNavigated;

        /// <summary>
        /// Fired when user clicks a breadcrumb segment.
        /// </summary>
        public event EventHandler<BreadcrumbClickEventArgs>? BreadcrumbSegmentClicked;

        /// <summary>
        /// Fired when user clicks a breadcrumb chevron (for dropdown).
        /// </summary>
        public event EventHandler<BreadcrumbClickEventArgs>? BreadcrumbChevronClicked;

        #endregion

        #region Public Methods

        /// <summary>
        /// Programmatically enter edit mode (for Ctrl+L shortcut).
        /// </summary>
        public void EnterEditMode()
        {
            ShowEditMode();
        }

        /// <summary>
        /// Programmatically exit edit mode.
        /// </summary>
        public void ExitEditMode()
        {
            ShowBreadcrumbMode();
        }

        /// <summary>
        /// Force-refresh the ItemsSource (for cases where collection reference changed while control was collapsed).
        /// </summary>
        public void RefreshItemsSource()
        {
            BreadcrumbRepeater.ItemsSource = null;
            BreadcrumbRepeater.ItemsSource = PathSegments;
        }

        #endregion

        #region Edit Mode

        private void ShowEditMode()
        {
            if (_isEditMode) return;
            _isEditMode = true;

            BreadcrumbScroller.Visibility = Visibility.Collapsed;
            OverflowIndicator.Visibility = Visibility.Collapsed;
            AutoSuggest.Visibility = Visibility.Visible;
            SetParentContainerFocusBorder(true);
            // archive:// 프리픽스 제거하여 Windows 탐색기 스타일 표시
            var displayPath = CurrentPath ?? string.Empty;
            if (Helpers.ArchivePathHelper.IsArchivePath(displayPath))
                displayPath = displayPath.Substring(Helpers.ArchivePathHelper.Prefix.Length);
            AutoSuggest.Text = displayPath;

            // Collapsed→Visible 전환 후 내부 TextBox 비주얼 트리가 materialize될 때까지
            // 한 프레임 대기. UpdateLayout()은 AccessViolation 위험이 있어 사용하지 않음.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                var textBox = FindDescendant<TextBox>(AutoSuggest);
                if (textBox != null)
                {
                    textBox.Focus(FocusState.Programmatic);
                    textBox.SelectAll();
                }
                else
                {
                    // TextBox가 아직 생성 안 된 경우: Focus로 트리 생성 촉진 후 재시도
                    AutoSuggest.Focus(FocusState.Programmatic);
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        var tb = FindDescendant<TextBox>(AutoSuggest);
                        if (tb != null)
                        {
                            tb.Focus(FocusState.Programmatic);
                            tb.SelectAll();
                        }
                    });
                }
            });
        }

        private void ShowBreadcrumbMode()
        {
            if (!_isEditMode) return;
            _isEditMode = false;

            AutoSuggest.Visibility = Visibility.Collapsed;
            AutoSuggest.ItemsSource = null;
            BreadcrumbScroller.Visibility = Visibility.Visible;
            SetParentContainerFocusBorder(false);

            // Restore overflow indicator after edit mode exit
            DispatcherQueue.TryEnqueue(() => UpdateOverflow(BreadcrumbScroller));
        }

        #endregion

        #region Event Handlers — Container

        private void OnContainerTapped(object sender, TappedRoutedEventArgs e)
        {
            // Only enter edit mode when clicking empty space (not on buttons/repeater items)
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element != this)
            {
                if (element is Button || element is ItemsRepeater) return;
                element = VisualTreeHelper.GetParent(element);
            }

            e.Handled = true; // tap이 AutoSuggestBox의 TextBox로 전파되어 SelectAll을 해제하는 것을 방지
            ShowEditMode();
        }

        private void OnRightPadTapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowEditMode();
        }

        #endregion

        #region Event Handlers — Breadcrumb

        private void OnSegmentClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fullPath)
                BreadcrumbSegmentClicked?.Invoke(this, new BreadcrumbClickEventArgs(fullPath, btn));
        }

        private void OnChevronClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fullPath)
                BreadcrumbChevronClicked?.Invoke(this, new BreadcrumbClickEventArgs(fullPath, btn));
        }

        #endregion

        #region Event Handlers — AutoSuggestBox

        private void OnAutoSuggestTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var text = sender.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                sender.ItemsSource = null;
                return;
            }

            var expanded = Environment.ExpandEnvironmentVariables(text);

            try
            {
                string? parentDir;
                string prefix;

                if (expanded.EndsWith('\\') || expanded.EndsWith('/'))
                {
                    parentDir = expanded;
                    prefix = string.Empty;
                }
                else
                {
                    parentDir = System.IO.Path.GetDirectoryName(expanded);
                    prefix = System.IO.Path.GetFileName(expanded);
                }

                if (string.IsNullOrEmpty(parentDir) || !System.IO.Directory.Exists(parentDir))
                {
                    if (text.Length <= 2)
                    {
                        var drives = System.IO.DriveInfo.GetDrives()
                            .Where(d => d.IsReady && d.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                            .Select(d => d.Name)
                            .Take(10)
                            .ToList();
                        sender.ItemsSource = drives.Count > 0 ? drives : null;
                    }
                    else
                    {
                        sender.ItemsSource = null;
                    }
                    return;
                }

                var suggestions = new System.IO.DirectoryInfo(parentDir)
                    .GetDirectories()
                    .Where(d => (d.Attributes & System.IO.FileAttributes.Hidden) == 0)
                    .Where(d => string.IsNullOrEmpty(prefix) || d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name)
                    .Take(10)
                    .Select(d => d.FullName)
                    .ToList();

                sender.ItemsSource = suggestions.Count > 0 ? suggestions : null;
            }
            catch
            {
                sender.ItemsSource = null;
            }
        }

        private void OnAutoSuggestSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string path)
                sender.Text = path;
        }

        private void OnAutoSuggestQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var path = args.QueryText?.Trim();
            if (string.IsNullOrEmpty(path)) return;

            path = Environment.ExpandEnvironmentVariables(path);
            PathNavigated?.Invoke(this, path);
            ShowBreadcrumbMode();
        }

        private void OnAutoSuggestKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ShowBreadcrumbMode();
                e.Handled = true;
            }
        }

        private void OnAutoSuggestLostFocus(object sender, RoutedEventArgs e)
        {
            // Delay to allow suggestion popup clicks to process
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isEditMode) return;

                // Check if focus moved to the suggestion popup or still within AutoSuggest
                var focused = FocusManager.GetFocusedElement(this.XamlRoot) as DependencyObject;
                if (focused != null && IsDescendantOf(focused, AutoSuggest))
                    return;

                // Also check if AutoSuggestBox itself still has focus
                if (AutoSuggest.FocusState != FocusState.Unfocused)
                    return;

                ShowBreadcrumbMode();
            });
        }

        #endregion

        #region Event Handlers — Scroll / Overflow

        private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ChangeView(sv.ScrollableWidth, null, null, true);
                DispatcherQueue.TryEnqueue(() => UpdateOverflow(sv));
            }
        }

        private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // BreadcrumbRepeater is inside a StackPanel inside BreadcrumbScroller
            var sv = BreadcrumbScroller;
            sv.ChangeView(sv.ScrollableWidth, null, null, true);
            DispatcherQueue.TryEnqueue(() => UpdateOverflow(sv));
        }

        private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
                UpdateOverflow(sv);
        }

        private void UpdateOverflow(ScrollViewer sv)
        {
            OverflowIndicator.Visibility = sv.HorizontalOffset > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        #endregion

        #region Helpers

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// 편집 모드 진입/종료 시 부모 컨테이너(AddressBarContainer)에 accent border를 표시/숨김.
        /// AutoSuggestBox 내부 TextBox의 focus border는 UserControl.Resources에서 비활성화했으므로,
        /// 부모 컨테이너에 직접 border를 적용하여 CornerRadius 클리핑 문제를 회피.
        /// TextControlBorderBrushFocused를 사용하여 커스텀 테마와 동일한 색상을 보장.
        /// </summary>
        private void SetParentContainerFocusBorder(bool show)
        {
            // 부모 컨테이너(Grid)를 찾아 BorderBrush/BorderThickness 적용
            var parent = VisualTreeHelper.GetParent(this);
            while (parent != null)
            {
                if (parent is Grid grid && grid.Name?.Contains("AddressBar") == true
                    || parent is Grid g2 && g2.Name?.Contains("PathHeader") == true)
                {
                    if (parent is Grid container)
                    {
                        if (show)
                        {
                            container.BorderBrush = GetFocusBorderBrush();
                            container.BorderThickness = new Thickness(1);
                        }
                        else
                        {
                            container.BorderBrush = null;
                            container.BorderThickness = new Thickness(0);
                        }
                    }
                    return;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        /// <summary>
        /// WinUI 3 TextBox 포커스 보더와 동일한 브러시를 반환.
        /// 커스텀 테마 적용 시 윈도우 레벨 ThemeDictionaries를 우선 조회.
        /// </summary>
        private Brush GetFocusBorderBrush()
        {
            const string key = "TextControlBorderBrushFocused";
            try
            {
                // 윈도우 레벨 ThemeDictionaries 우선 (커스텀 테마 반영)
                if (this.XamlRoot?.Content is FrameworkElement root)
                {
                    var themeKey = root.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
                    if (root.Resources.ThemeDictionaries.TryGetValue(themeKey, out var dict)
                        && dict is ResourceDictionary rd
                        && rd.TryGetValue(key, out var val)
                        && val is Brush brush)
                    {
                        return brush;
                    }
                }
            }
            catch { }

            // 앱 레벨 fallback (기본 Dark/Light 테마)
            try { return (Brush)Application.Current.Resources[key]; } catch { }
            return new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }

        #endregion
    }

    /// <summary>
    /// Event args for breadcrumb segment/chevron clicks.
    /// </summary>
    public class BreadcrumbClickEventArgs : EventArgs
    {
        public string FullPath { get; }
        public FrameworkElement SourceButton { get; }

        public BreadcrumbClickEventArgs(string fullPath, FrameworkElement sourceButton)
        {
            FullPath = fullPath;
            SourceButton = sourceButton;
        }
    }
}
