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
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is GridView gridView)
            {
                ViewModel.CurrentFolder.SyncSelectedItems(gridView.SelectedItems);
            }
        }

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
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (e.OriginalSource is FrameworkElement fe && ContextMenuService != null && ContextMenuHost != null)
            {
                Microsoft.UI.Xaml.Controls.MenuFlyout? flyout = null;

                if (fe.DataContext is FolderViewModel folder)
                    flyout = ContextMenuService.BuildFolderMenu(folder, ContextMenuHost);
                else if (fe.DataContext is FileViewModel file)
                    flyout = ContextMenuService.BuildFileMenu(file, ContextMenuHost);

                if (flyout != null)
                {
                    flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = e.GetPosition(fe)
                    });
                    e.Handled = true;
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
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[IconModeView] DoubleClick: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[IconModeView] DoubleClick: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[IconModeView] Error opening file: {ex.Message}");
                }
            }
        }

        private void OnIconKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
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
                    HandleIconEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    // Navigate to parent folder
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    Helpers.DebugLogger.Log("[IconModeView] Backspace: Navigating to parent folder");
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Let global handler handle Delete
                    break;

                case Windows.System.VirtualKey.F2:
                    // Let global handler handle F2 (Rename)
                    break;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                    // GridView handles arrow key navigation by default
                    break;
            }
        }

        private void HandleIconEnter()
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[IconModeView] Enter: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[IconModeView] Enter: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[IconModeView] Error opening file: {ex.Message}");
                }
            }
        }

        // Ctrl+Mouse Wheel view mode cycling is handled globally by MainWindow.OnGlobalPointerWheelChanged

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
