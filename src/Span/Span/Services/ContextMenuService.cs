using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    public class ContextMenuService
    {
        private readonly ShellService _shellService;

        public ContextMenuService(ShellService shellService)
        {
            _shellService = shellService;
        }

        public MenuFlyout BuildFileMenu(FileViewModel file, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // Open
            menu.Items.Add(CreateItem("열기", "\uE8E5", () => host.PerformOpen(file)));
            // Open With
            menu.Items.Add(CreateItem("연결 프로그램...", "\uE7AC", () => _shellService.OpenWithAsync(file.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Cut / Copy
            menu.Items.Add(CreateItem("잘라내기", "\uE8C6", () => host.PerformCut(file.Path)));
            menu.Items.Add(CreateItem("복사", "\uE8C8", () => host.PerformCopy(file.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Delete / Rename
            menu.Items.Add(CreateItem("삭제", "\uE74D", () => host.PerformDelete(file.Path, file.Name)));
            menu.Items.Add(CreateItem("이름 바꾸기", "\uE70F", () => host.PerformRename(file)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Copy Path / Open in Explorer
            menu.Items.Add(CreateItem("경로 복사", "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path)));
            menu.Items.Add(CreateItem("파일 탐색기에서 열기", "\uED25", () => _shellService.OpenInExplorer(file.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Properties
            menu.Items.Add(CreateItem("속성", "\uE946", () => _shellService.ShowProperties(file.Path)));

            return menu;
        }

        public MenuFlyout BuildFolderMenu(FolderViewModel folder, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // Open
            menu.Items.Add(CreateItem("열기", "\uE8E5", () => host.PerformOpen(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Cut / Copy / Paste
            menu.Items.Add(CreateItem("잘라내기", "\uE8C6", () => host.PerformCut(folder.Path)));
            menu.Items.Add(CreateItem("복사", "\uE8C8", () => host.PerformCopy(folder.Path)));
            menu.Items.Add(CreateItem("붙여넣기", "\uE77F", () => host.PerformPaste(folder.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Delete / Rename
            menu.Items.Add(CreateItem("삭제", "\uE74D", () => host.PerformDelete(folder.Path, folder.Name)));
            menu.Items.Add(CreateItem("이름 바꾸기", "\uE70F", () => host.PerformRename(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Favorites
            bool isFav = host.IsFavorite(folder.Path);
            if (isFav)
                menu.Items.Add(CreateItem("즐겨찾기에서 제거", "\uE735", () => host.RemoveFromFavorites(folder.Path)));
            else
                menu.Items.Add(CreateItem("즐겨찾기에 추가", "\uE734", () => host.AddToFavorites(folder.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Copy Path / Open in Explorer
            menu.Items.Add(CreateItem("경로 복사", "\uE8C8", () => _shellService.CopyPathToClipboard(folder.Path)));
            menu.Items.Add(CreateItem("파일 탐색기에서 열기", "\uED25", () => _shellService.OpenInExplorer(folder.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Properties
            menu.Items.Add(CreateItem("속성", "\uE946", () => _shellService.ShowProperties(folder.Path)));

            return menu;
        }

        public MenuFlyout BuildDriveMenu(DriveItem drive, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // Open
            menu.Items.Add(CreateItem("열기", "\uE8E5", () => host.PerformOpenDrive(drive)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Copy Path / Open in Explorer
            menu.Items.Add(CreateItem("경로 복사", "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path)));
            menu.Items.Add(CreateItem("파일 탐색기에서 열기", "\uED25", () => _shellService.OpenInExplorer(drive.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Properties
            menu.Items.Add(CreateItem("속성", "\uE946", () => _shellService.ShowProperties(drive.Path)));

            return menu;
        }

        public MenuFlyout BuildFavoriteMenu(FavoriteItem fav, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // Open
            menu.Items.Add(CreateItem("열기", "\uE8E5", () => host.PerformOpenFavorite(fav)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Remove from Favorites
            menu.Items.Add(CreateItem("즐겨찾기에서 제거", "\uE735", () => host.RemoveFromFavorites(fav.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Copy Path / Open in Explorer
            menu.Items.Add(CreateItem("경로 복사", "\uE8C8", () => _shellService.CopyPathToClipboard(fav.Path)));
            menu.Items.Add(CreateItem("파일 탐색기에서 열기", "\uED25", () => _shellService.OpenInExplorer(fav.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // Properties
            menu.Items.Add(CreateItem("속성", "\uE946", () => _shellService.ShowProperties(fav.Path)));

            return menu;
        }

        public MenuFlyout BuildEmptyAreaMenu(string folderPath, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            // New Folder / Paste
            menu.Items.Add(CreateItem("새 폴더", "\uE8B7", () => host.PerformNewFolder(folderPath)));
            menu.Items.Add(CreateItem("붙여넣기", "\uE77F", () => host.PerformPaste(folderPath)));
            menu.Items.Add(new MenuFlyoutSeparator());

            // View submenu
            var viewSub = new MenuFlyoutSubItem { Text = "보기", Icon = new FontIcon { Glyph = "\uE8FD" } };
            viewSub.Items.Add(CreateItem("Miller Columns", "\uF0E2", () => host.SwitchViewMode(ViewMode.MillerColumns)));
            viewSub.Items.Add(CreateItem("Details", "\uE8EF", () => host.SwitchViewMode(ViewMode.Details)));
            viewSub.Items.Add(new MenuFlyoutSeparator());
            viewSub.Items.Add(CreateItem("Extra Large Icons", null, () => host.SwitchViewMode(ViewMode.IconExtraLarge)));
            viewSub.Items.Add(CreateItem("Large Icons", null, () => host.SwitchViewMode(ViewMode.IconLarge)));
            viewSub.Items.Add(CreateItem("Medium Icons", null, () => host.SwitchViewMode(ViewMode.IconMedium)));
            viewSub.Items.Add(CreateItem("Small Icons", null, () => host.SwitchViewMode(ViewMode.IconSmall)));
            menu.Items.Add(viewSub);

            // Sort submenu
            var sortSub = new MenuFlyoutSubItem { Text = "정렬", Icon = new FontIcon { Glyph = "\uE8CB" } };
            sortSub.Items.Add(CreateItem("이름", "\uE8C1", () => host.ApplySort("Name")));
            sortSub.Items.Add(CreateItem("날짜", "\uE787", () => host.ApplySort("Date")));
            sortSub.Items.Add(CreateItem("크기", "\uE91B", () => host.ApplySort("Size")));
            sortSub.Items.Add(CreateItem("종류", "\uE8FD", () => host.ApplySort("Type")));
            sortSub.Items.Add(new MenuFlyoutSeparator());
            sortSub.Items.Add(CreateItem("오름차순", "\uE74A", () => host.ApplySortDirection(true)));
            sortSub.Items.Add(CreateItem("내림차순", "\uE74B", () => host.ApplySortDirection(false)));
            menu.Items.Add(sortSub);

            return menu;
        }

        private static MenuFlyoutItem CreateItem(string text, string? glyph, System.Action action)
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
