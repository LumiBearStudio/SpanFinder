using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    public class ContextMenuService
    {
        private readonly ShellService _shellService;
        private readonly LocalizationService _loc;

        /// <summary>Current shell menu session (kept alive while menu is open)</summary>
        private ShellContextMenu.Session? _currentSession;

        /// <summary>HWND of the owner window (set by MainWindow)</summary>
        public IntPtr OwnerHwnd { get; set; }

        public ContextMenuService(ShellService shellService, LocalizationService localizationService)
        {
            _shellService = shellService;
            _loc = localizationService;
        }

        public MenuFlyout BuildFileMenu(FileViewModel file, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // Custom localized items
            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(file)));
            menu.Items.Add(CreateItem(_loc.Get("OpenWith"), "\uE7AC", () => _shellService.OpenWithAsync(file.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(file.Path)));
            menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(file.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(file.Path, file.Name)));
            menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(file)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path)));
            menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(file.Path)));

            // Shell extension items
            AppendShellExtensionItems(menu, file.Path);

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(file.Path)));

            // Cleanup session when menu closes
            menu.Closed += OnMenuClosed;

            return menu;
        }

        public MenuFlyout BuildFolderMenu(FolderViewModel folder, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(folder.Path)));
            menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(folder.Path)));
            menu.Items.Add(CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folder.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(folder.Path, folder.Name)));
            menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            bool isFav = host.IsFavorite(folder.Path);
            if (isFav)
                menu.Items.Add(CreateItem(_loc.Get("RemoveFromFavorites"), "\uE735", () => host.RemoveFromFavorites(folder.Path)));
            else
                menu.Items.Add(CreateItem(_loc.Get("AddToFavorites"), "\uE734", () => host.AddToFavorites(folder.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(folder.Path)));
            menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(folder.Path)));

            // Shell extension items
            AppendShellExtensionItems(menu, folder.Path);

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(folder.Path)));

            menu.Closed += OnMenuClosed;

            return menu;
        }

        public MenuFlyout BuildDriveMenu(DriveItem drive, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpenDrive(drive)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path)));
            menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(drive.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(drive.Path)));

            return menu;
        }

        public MenuFlyout BuildFavoriteMenu(FavoriteItem fav, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpenFavorite(fav)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("RemoveFromFavorites"), "\uE735", () => host.RemoveFromFavorites(fav.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(fav.Path)));
            menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(fav.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(fav.Path)));

            return menu;
        }

        public MenuFlyout BuildEmptyAreaMenu(string folderPath, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("NewFolder"), "\uE8B7", () => host.PerformNewFolder(folderPath)));
            menu.Items.Add(CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folderPath)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // View submenu
            var viewSub = new MenuFlyoutSubItem { Text = _loc.Get("View"), Icon = new FontIcon { Glyph = "\uE8FD" } };
            viewSub.Items.Add(CreateItem(_loc.Get("MillerColumns"), "\uF0E2", () => host.SwitchViewMode(ViewMode.MillerColumns)));
            viewSub.Items.Add(CreateItem(_loc.Get("Details"), "\uE8EF", () => host.SwitchViewMode(ViewMode.Details)));
            viewSub.Items.Add(new MenuFlyoutSeparator());
            viewSub.Items.Add(CreateItem(_loc.Get("ExtraLargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconExtraLarge)));
            viewSub.Items.Add(CreateItem(_loc.Get("LargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconLarge)));
            viewSub.Items.Add(CreateItem(_loc.Get("MediumIcons"), null, () => host.SwitchViewMode(ViewMode.IconMedium)));
            viewSub.Items.Add(CreateItem(_loc.Get("SmallIcons"), null, () => host.SwitchViewMode(ViewMode.IconSmall)));
            menu.Items.Add(viewSub);

            // Sort submenu
            var sortSub = new MenuFlyoutSubItem { Text = _loc.Get("Sort"), Icon = new FontIcon { Glyph = "\uE8CB" } };
            sortSub.Items.Add(CreateItem(_loc.Get("Name"), "\uE8C1", () => host.ApplySort("Name")));
            sortSub.Items.Add(CreateItem(_loc.Get("Date"), "\uE787", () => host.ApplySort("Date")));
            sortSub.Items.Add(CreateItem(_loc.Get("Size"), "\uE91B", () => host.ApplySort("Size")));
            sortSub.Items.Add(CreateItem(_loc.Get("Type"), "\uE8FD", () => host.ApplySort("Type")));
            sortSub.Items.Add(new MenuFlyoutSeparator());
            sortSub.Items.Add(CreateItem(_loc.Get("Ascending"), "\uE74A", () => host.ApplySortDirection(true)));
            sortSub.Items.Add(CreateItem(_loc.Get("Descending"), "\uE74B", () => host.ApplySortDirection(false)));
            menu.Items.Add(sortSub);

            return menu;
        }

        /// <summary>
        /// Enumerate shell extension items for the given path and append them to the menu.
        /// Standard items (open, copy, delete, etc.) are filtered out — only third-party
        /// extensions (Bandizip, 7-Zip, VS Code, etc.) are added.
        /// </summary>
        private void AppendShellExtensionItems(MenuFlyout menu, string path)
        {
            if (OwnerHwnd == IntPtr.Zero) return;

            try
            {
                // Dispose previous session
                _currentSession?.Dispose();
                _currentSession = ShellContextMenu.CreateSession(OwnerHwnd, path);

                if (_currentSession == null || _currentSession.Items.Count == 0)
                    return;

                menu.Items.Add(new MenuFlyoutSeparator());

                foreach (var shellItem in _currentSession.Items)
                {
                    var flyoutItem = ConvertShellItem(shellItem);
                    if (flyoutItem != null)
                    {
                        menu.Items.Add(flyoutItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] Shell extension enumeration error: {ex.Message}");
            }
        }

        /// <summary>Convert a ShellMenuItem to a WinUI MenuFlyoutItemBase.</summary>
        private MenuFlyoutItemBase? ConvertShellItem(ShellMenuItem shellItem)
        {
            if (shellItem.IsSeparator)
                return new MenuFlyoutSeparator();

            if (shellItem.HasSubmenu)
            {
                var subItem = new MenuFlyoutSubItem { Text = shellItem.Text };
                foreach (var child in shellItem.Children!)
                {
                    var childItem = ConvertShellItem(child);
                    if (childItem != null)
                        subItem.Items.Add(childItem);
                }
                return subItem.Items.Count > 0 ? subItem : null;
            }

            if (string.IsNullOrWhiteSpace(shellItem.Text))
                return null;

            var item = new MenuFlyoutItem { Text = shellItem.Text };
            item.IsEnabled = !shellItem.IsDisabled;

            // Capture commandId and session reference for the click handler
            int cmdId = shellItem.CommandId;
            var session = _currentSession;
            item.Click += (s, e) => session?.InvokeCommand(cmdId);

            return item;
        }

        private void OnMenuClosed(object? sender, object e)
        {
            // Dispose shell COM session when menu closes
            _currentSession?.Dispose();
            _currentSession = null;

            if (sender is MenuFlyout flyout)
                flyout.Closed -= OnMenuClosed;
        }

        private static MenuFlyoutItem CreateItem(string text, string? glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            if (glyph != null)
            {
                item.Icon = new FontIcon { Glyph = glyph };
            }
            item.Click += (s, e) => action();
            return item;
        }
    }
}
