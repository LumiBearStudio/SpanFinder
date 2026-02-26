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

        public AddressBarControl()
        {
            this.InitializeComponent();
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
            AutoSuggest.Text = CurrentPath ?? string.Empty;
            AutoSuggest.Focus(FocusState.Keyboard);

            // Select all text after focus
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var textBox = FindDescendant<TextBox>(AutoSuggest);
                textBox?.SelectAll();
            });
        }

        private void ShowBreadcrumbMode()
        {
            if (!_isEditMode) return;
            _isEditMode = false;

            AutoSuggest.Visibility = Visibility.Collapsed;
            AutoSuggest.ItemsSource = null;
            BreadcrumbScroller.Visibility = Visibility.Visible;
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
            if (sender is FrameworkElement fe && fe.Parent is ScrollViewer sv)
            {
                sv.ChangeView(sv.ScrollableWidth, null, null, true);
                DispatcherQueue.TryEnqueue(() => UpdateOverflow(sv));
            }
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
