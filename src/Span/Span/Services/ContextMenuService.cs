using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    public class ContextMenuService
    {
        private readonly ShellService _shellService;
        private readonly LocalizationService _loc;
        private readonly SettingsService _settings;

        /// <summary>Current shell menu session (kept alive while menu is open)</summary>
        private ShellContextMenu.Session? _currentSession;

        /// <summary>HWND of the owner window (set by MainWindow)</summary>
        public IntPtr OwnerHwnd { get; set; }

        /// <summary>Lazy XamlRoot provider (Content.XamlRoot is only available after Loaded)</summary>
        public Func<Microsoft.UI.Xaml.XamlRoot?>? XamlRootProvider { get; set; }

        #region Shell Translation Tables (per-language)

        /// <summary>
        /// Verb-based translations per language. Maps canonical verb → localized text.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> ShellVerbTranslations = new()
        {
            ["ko"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sendto"] = "보내기",
                ["pintohome"] = "빠른 실행에 고정",
                ["pintostartscreen"] = "시작 화면에 고정",
                ["unpinfromhome"] = "빠른 실행에서 제거",
                ["Windows.share"] = "공유",
                ["previousversions"] = "이전 버전 복원",
            },
            ["ja"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sendto"] = "送る",
                ["pintohome"] = "クイック アクセスにピン留め",
                ["pintostartscreen"] = "スタートにピン留めする",
                ["unpinfromhome"] = "クイック アクセスからピン留めを外す",
                ["Windows.share"] = "共有",
                ["previousversions"] = "以前のバージョンの復元",
            },
        };

        /// <summary>
        /// Text-based translations per language. Maps English text → localized text.
        /// Covers top-level items and common sub-menu items (Send to, Share, etc.).
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> ShellTextTranslations = new()
        {
            ["ko"] = new(StringComparer.OrdinalIgnoreCase)
            {
                // Top-level shell items
                ["Send to"] = "보내기",
                ["Give access to"] = "액세스 권한 부여",
                ["Include in library"] = "라이브러리에 포함",
                ["Pin to Start"] = "시작 화면에 고정",
                ["Pin to Quick access"] = "빠른 실행에 고정",
                ["Pin to Quick Access"] = "빠른 실행에 고정",
                ["Restore previous versions"] = "이전 버전 복원",
                ["Share"] = "공유",
                ["Share with"] = "공유 대상",
                ["Cast to Device"] = "디바이스로 캐스트",
                ["Cast to device"] = "디바이스로 캐스트",
                ["Scan with Microsoft Defender..."] = "Microsoft Defender로 검사...",
                ["Edit with Notepad"] = "메모장에서 편집",
                ["Print"] = "인쇄",

                // Copilot items
                ["Ask Copilot"] = "Copilot에게 질문하기",

                // Send to sub-items
                ["Desktop (create shortcut)"] = "바탕 화면에 바로 가기 만들기",
                ["Desktop (Create Shortcut)"] = "바탕 화면에 바로 가기 만들기",
                ["Mail recipient"] = "메일 수신자",
                ["Mail Recipient"] = "메일 수신자",
                ["Compressed (zipped) folder"] = "압축(zip) 폴더",
                ["Compressed (zipped) Folder"] = "압축(zip) 폴더",
                ["Bluetooth device"] = "Bluetooth 장치",
                ["Bluetooth Device"] = "Bluetooth 장치",
                ["Documents"] = "문서",
                ["Fax recipient"] = "팩스 수신자",
                ["Fax Recipient"] = "팩스 수신자",

                // Give access to sub-items
                ["Specific people..."] = "특정 사용자...",
                ["Stop sharing"] = "공유 중지",
                ["Remove access"] = "액세스 제거",

                // Include in library sub-items
                ["Documents library"] = "문서 라이브러리",
                ["Music library"] = "음악 라이브러리",
                ["Pictures library"] = "사진 라이브러리",
                ["Videos library"] = "비디오 라이브러리",

                // Share sub-items (common sharing targets)
                ["Email"] = "이메일",
                ["Nearby sharing"] = "근거리 공유",
                ["Copy link"] = "링크 복사",
            },
            ["ja"] = new(StringComparer.OrdinalIgnoreCase)
            {
                // Top-level shell items
                ["Send to"] = "送る",
                ["Give access to"] = "アクセスを許可する",
                ["Include in library"] = "ライブラリに追加",
                ["Pin to Start"] = "スタートにピン留めする",
                ["Pin to Quick access"] = "クイック アクセスにピン留め",
                ["Pin to Quick Access"] = "クイック アクセスにピン留め",
                ["Restore previous versions"] = "以前のバージョンの復元",
                ["Share"] = "共有",
                ["Share with"] = "共有",
                ["Cast to Device"] = "デバイスにキャスト",
                ["Cast to device"] = "デバイスにキャスト",
                ["Scan with Microsoft Defender..."] = "Microsoft Defenderでスキャン...",
                ["Edit with Notepad"] = "メモ帳で編集",
                ["Print"] = "印刷",

                // Copilot items
                ["Ask Copilot"] = "Copilotに質問する",

                // Send to sub-items
                ["Desktop (create shortcut)"] = "デスクトップ (ショートカットを作成)",
                ["Desktop (Create Shortcut)"] = "デスクトップ (ショートカットを作成)",
                ["Mail recipient"] = "メール受信者",
                ["Mail Recipient"] = "メール受信者",
                ["Compressed (zipped) folder"] = "圧縮(zip)フォルダー",
                ["Compressed (zipped) Folder"] = "圧縮(zip)フォルダー",
                ["Bluetooth device"] = "Bluetoothデバイス",
                ["Bluetooth Device"] = "Bluetoothデバイス",
                ["Documents"] = "ドキュメント",
                ["Fax recipient"] = "FAX受信者",
                ["Fax Recipient"] = "FAX受信者",

                // Give access to sub-items
                ["Specific people..."] = "特定のユーザー...",
                ["Stop sharing"] = "共有の停止",
                ["Remove access"] = "アクセスの削除",

                // Include in library sub-items
                ["Documents library"] = "ドキュメント ライブラリ",
                ["Music library"] = "ミュージック ライブラリ",
                ["Pictures library"] = "ピクチャ ライブラリ",
                ["Videos library"] = "ビデオ ライブラリ",

                // Share sub-items
                ["Email"] = "メール",
                ["Nearby sharing"] = "近距離共有",
                ["Copy link"] = "リンクのコピー",
            },
        };

        #endregion

        #region Windows Shell Extras (Share, Include in library, Pin to Start, etc.)

        /// <summary>
        /// Verbs identifying "Windows shell extras" items (Share, Pin to Start, etc.).
        /// Hidden when ShowWindowsShellExtras setting is OFF.
        /// </summary>
        private static readonly HashSet<string> WindowsShellExtraVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows.share",
            "previousversions",
            "pintostartscreen",
            "pintohome",
            "unpinfromhome",
        };

        /// <summary>
        /// Text patterns identifying "Windows shell extras" items (when verb is unavailable).
        /// </summary>
        private static readonly string[] WindowsShellExtraTexts =
        {
            "Share",                      "공유",
            "Restore previous versions",  "이전 버전 복원",
            "Include in library",         "라이브러리에 포함",
            "Pin to Start",               "시작 화면에 고정",
            "시작화면에 고정",
            "Pin to Quick access",        "빠른 실행에 고정",
            "Pin to Quick Access",        "빠른 실행에 고정",
            "Unpin from Quick access",    "빠른 실행에서 제거",
            "Give access to",             "액세스 권한 부여",
            "Send to",                    "보내기",
        };

        #endregion

        #region Copilot items (hidden when ShowCopilotMenu is OFF)

        /// <summary>
        /// Text patterns identifying Copilot context menu items.
        /// Hidden when ShowCopilotMenu setting is OFF.
        /// </summary>
        private static readonly string[] CopilotTextPatterns =
        {
            "copilot",          // "Ask Copilot", "Microsoft 365 Copilot..."
        };

        #endregion

        /// <summary>
        /// Text patterns that identify developer tool context menu items.
        /// Case-insensitive matching against menu item text.
        /// </summary>
        private static readonly string[] DeveloperTextPatterns =
        {
            "git",              // Git GUI, Git Bash, TortoiseGit
            "visual studio",    // Visual Studio
            "open with code",   // VS Code (English)
            "code(으)로 열기",   // VS Code (Korean)
            "code로 열기",       // VS Code (Korean variant)
            "tortoise",         // TortoiseGit, TortoiseSVN
            "svn",              // Subversion
            "sublime",          // Sublime Text
            "notepad++",        // Notepad++
            "winmerge",         // WinMerge
            "beyond compare",   // Beyond Compare
            "node.js",          // Node.js
            "edit with idle",   // Python IDLE
        };

        #region Edit-with grouping (group "Edit with X" items into submenu)

        /// <summary>
        /// Text patterns identifying "edit with program" shell items.
        /// When 2+ items match, they are grouped into a single submenu.
        /// </summary>
        private static readonly string[] EditWithTextPatterns =
        {
            "편집",               // Korean: 사진으로 편집, 그림판으로 편집, 메모장에서 편집
            "Edit with",         // English: Edit with Photos, Edit with Paint
            "Edit in",           // English variant
            "で編集",             // Japanese: ペイントで編集, メモ帳で編集
            "Designer",          // Microsoft Designer (all languages)
        };

        #endregion

        public ContextMenuService(ShellService shellService, LocalizationService localizationService, SettingsService settingsService)
        {
            _shellService = shellService;
            _loc = localizationService;
            _settings = settingsService;
        }

        public async Task<MenuFlyout> BuildFileMenuAsync(FileViewModel file, IContextMenuHost host)
        {
            var menu = new MenuFlyout();
            bool isRemote = FileSystemRouter.IsRemotePath(file.Path);

            if (isRemote)
            {
                // ── Remote file menu (FTP/SFTP) ──
                menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(file.Path)));
                menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(file.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(file.Path, file.Name)));
                menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(file)));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(file)));
            }
            else
            {
                // ── Local file menu ──
                menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(file)));
                menu.Items.Add(CreateItem(_loc.Get("OpenWith"), "\uE7AC", () => _ = _shellService.OpenWithAsync(file.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(file.Path)));
                menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(file.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());

                // Compress / Extract
                string ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                if (ext == ".zip")
                {
                    menu.Items.Add(CreateItem(_loc.Get("ExtractHere"), "\uE8B7", () => host.PerformExtractHere(file.Path)));
                    menu.Items.Add(CreateItem(_loc.Get("ExtractTo"), "\uE8B7", () => host.PerformExtractTo(file.Path)));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }
                menu.Items.Add(CreateItem(_loc.Get("CompressToZip"), "\uE8C5", () => host.PerformCompress(new[] { file.Path })));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(file.Path, file.Name)));
                menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(file)));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path)));
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(file.Path)));

                // Shell extension items (loaded before menu is shown)
                await AppendShellExtensionItemsAsync(menu, file.Path);

                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(file)));
            }

            // Cleanup session when menu closes
            menu.Closed += OnMenuClosed;

            return menu;
        }

        public async Task<MenuFlyout> BuildFolderMenuAsync(FolderViewModel folder, IContextMenuHost host)
        {
            var menu = new MenuFlyout();
            bool isRemote = FileSystemRouter.IsRemotePath(folder.Path);

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(folder.Path)));
            menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(folder.Path)));
            var folderPaste = CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folder.Path));
            folderPaste.IsEnabled = host.HasClipboardContent;
            menu.Items.Add(folderPaste);
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!isRemote)
            {
                // Compress (local only)
                menu.Items.Add(CreateItem(_loc.Get("CompressToZip"), "\uE8C5", () => host.PerformCompress(new[] { folder.Path })));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(folder.Path, folder.Name)));
            menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(folder)));
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!isRemote)
            {
                bool isFav = host.IsFavorite(folder.Path);
                if (isFav)
                    menu.Items.Add(CreateItem(_loc.Get("RemoveFromFavorites"), "\uE735", () => host.RemoveFromFavorites(folder.Path)));
                else
                    menu.Items.Add(CreateItem(_loc.Get("AddToFavorites"), "\uE734", () => host.AddToFavorites(folder.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(folder.Path)));

            if (!isRemote)
            {
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(folder.Path)));
                // Shell extension items (loaded before menu is shown)
                await AppendShellExtensionItemsAsync(menu, folder.Path);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(folder)));

            menu.Closed += OnMenuClosed;

            return menu;
        }

        public MenuFlyout BuildDriveMenu(DriveItem drive, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpenDrive(drive)));

            // 용량 정보 (disabled label) — 로컬/리무버블/CD/네트워크 드라이브만
            if (!drive.IsRemoteConnection && !drive.IsCloudStorage && drive.TotalSize > 0)
            {
                var capItem = CreateItem(drive.SizeDescription, null, () => { });
                capItem.IsEnabled = false;
                menu.Items.Add(capItem);
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            if (drive.IsRemoteConnection)
            {
                // 원격 연결 (SFTP/FTP): 편집 + 제거
                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("EditConnection"), "\uE70F", () =>
                {
                    if (!string.IsNullOrEmpty(drive.ConnectionId))
                        host.EditRemoteConnection(drive.ConnectionId);
                }));
                menu.Items.Add(CreateItem(_loc.Get("RemoveConnection"), "\uE74D", () =>
                {
                    if (!string.IsNullOrEmpty(drive.ConnectionId))
                        host.RemoveRemoteConnection(drive.ConnectionId);
                }));
            }
            else if (drive.IsCloudStorage)
            {
                // 클라우드 스토리지: 경로 복사 + 탐색기
                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path)));
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(drive.Path)));
            }
            else
            {
                // 로컬/리무버블/CD/네트워크 매핑 드라이브
                bool isRemovableOrCdrom = drive.DriveType == "Removable" || drive.DriveType == "CDRom";
                bool isNetwork = drive.DriveType == "Network";

                // 꺼내기 (Removable / CDRom)
                if (isRemovableOrCdrom)
                {
                    menu.Items.Add(CreateItem(_loc.Get("Eject"), "\uE7E7", () => host.PerformEjectDrive(drive)));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }

                // 연결 끊기 (Network mapped)
                if (isNetwork)
                {
                    menu.Items.Add(CreateItem(_loc.Get("DisconnectDrive"), "\uE8CD", () => host.PerformDisconnectDrive(drive)));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path)));
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(drive.Path)));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(drive.Path)));
            }

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

            // New submenu: folder + common file types
            var newSub = new MenuFlyoutSubItem { Text = _loc.Get("New"), Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 } };
            ApplyCompact(newSub);
            newSub.Items.Add(CreateItem(_loc.Get("NewFolder"), "\uE8B7", () => host.PerformNewFolder(folderPath)));
            newSub.Items.Add(new MenuFlyoutSeparator());
            newSub.Items.Add(CreateItem(_loc.Get("NewTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Text Document.txt")));
            newSub.Items.Add(CreateItem(_loc.Get("NewWordDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Document.docx")));
            newSub.Items.Add(CreateItem(_loc.Get("NewExcelSpreadsheet"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Spreadsheet.xlsx")));
            newSub.Items.Add(CreateItem(_loc.Get("NewPowerPoint"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Presentation.pptx")));
            newSub.Items.Add(new MenuFlyoutSeparator());
            newSub.Items.Add(CreateItem(_loc.Get("NewBitmapImage"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Bitmap Image.bmp")));
            newSub.Items.Add(CreateItem(_loc.Get("NewRichTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Rich Text Document.rtf")));
            newSub.Items.Add(CreateItem(_loc.Get("NewZipArchive"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Compressed (zipped) Folder.zip")));
            menu.Items.Add(newSub);

            var emptyPaste = CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folderPath));
            emptyPaste.IsEnabled = host.HasClipboardContent;
            menu.Items.Add(emptyPaste);
            menu.Items.Add(new MenuFlyoutSeparator());

            // View submenu
            var viewSub = new MenuFlyoutSubItem { Text = _loc.Get("View"), Icon = new FontIcon { Glyph = "\uE8FD", FontSize = 14 } };
            ApplyCompact(viewSub);
            viewSub.Items.Add(CreateItem(_loc.Get("MillerColumns"), "\uF0E2", () => host.SwitchViewMode(ViewMode.MillerColumns)));
            viewSub.Items.Add(CreateItem(_loc.Get("Details"), "\uE8EF", () => host.SwitchViewMode(ViewMode.Details)));
            viewSub.Items.Add(new MenuFlyoutSeparator());
            viewSub.Items.Add(CreateItem(_loc.Get("ExtraLargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconExtraLarge)));
            viewSub.Items.Add(CreateItem(_loc.Get("LargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconLarge)));
            viewSub.Items.Add(CreateItem(_loc.Get("MediumIcons"), null, () => host.SwitchViewMode(ViewMode.IconMedium)));
            viewSub.Items.Add(CreateItem(_loc.Get("SmallIcons"), null, () => host.SwitchViewMode(ViewMode.IconSmall)));
            menu.Items.Add(viewSub);

            // Sort submenu
            var sortSub = new MenuFlyoutSubItem { Text = _loc.Get("Sort"), Icon = new FontIcon { Glyph = "\uE8CB", FontSize = 14 } };
            ApplyCompact(sortSub);
            sortSub.Items.Add(CreateItem(_loc.Get("Name"), "\uE8C1", () => host.ApplySort("Name")));
            sortSub.Items.Add(CreateItem(_loc.Get("Date"), "\uE787", () => host.ApplySort("Date")));
            sortSub.Items.Add(CreateItem(_loc.Get("Size"), "\uE91B", () => host.ApplySort("Size")));
            sortSub.Items.Add(CreateItem(_loc.Get("Type"), "\uE8FD", () => host.ApplySort("Type")));
            sortSub.Items.Add(new MenuFlyoutSeparator());
            sortSub.Items.Add(CreateItem(_loc.Get("Ascending"), "\uE74A", () => host.ApplySortDirection(true)));
            sortSub.Items.Add(CreateItem(_loc.Get("Descending"), "\uE74B", () => host.ApplySortDirection(false)));
            menu.Items.Add(sortSub);

            // Group By submenu
            var currentGroup = host.CurrentGroupBy;
            var groupSub = new MenuFlyoutSubItem { Text = _loc.Get("GroupBy"), Icon = new FontIcon { Glyph = "\uF168", FontSize = 14 } };
            ApplyCompact(groupSub);
            groupSub.Items.Add(CreateToggle(_loc.Get("None"), currentGroup == "None", () => host.ApplyGroupBy("None")));
            groupSub.Items.Add(CreateToggle(_loc.Get("Name"), currentGroup == "Name", () => host.ApplyGroupBy("Name")));
            groupSub.Items.Add(CreateToggle(_loc.Get("Type"), currentGroup == "Type", () => host.ApplyGroupBy("Type")));
            groupSub.Items.Add(CreateToggle(_loc.Get("Date"), currentGroup == "DateModified", () => host.ApplyGroupBy("DateModified")));
            groupSub.Items.Add(CreateToggle(_loc.Get("Size"), currentGroup == "Size", () => host.ApplyGroupBy("Size")));
            menu.Items.Add(groupSub);

            menu.Items.Add(new MenuFlyoutSeparator());

            // Selection submenu
            var selectSub = new MenuFlyoutSubItem { Text = _loc.Get("Select"), Icon = new FontIcon { Glyph = "\uE762", FontSize = 14 } };
            ApplyCompact(selectSub);
            selectSub.Items.Add(CreateItem(_loc.Get("SelectAll") + "  Ctrl+A", "\uE8B3", () => host.PerformSelectAll()));
            selectSub.Items.Add(CreateItem(_loc.Get("SelectNone") + "  Ctrl+Shift+A", null, () => host.PerformSelectNone()));
            selectSub.Items.Add(CreateItem(_loc.Get("InvertSelection") + "  Ctrl+I", null, () => host.PerformInvertSelection()));
            menu.Items.Add(selectSub);

            return menu;
        }

        /// <summary>
        /// Asynchronously load shell extension items and append them to the menu.
        /// This is awaited before the menu is shown, preventing visible flicker
        /// from items being added after display.
        /// Uses dedicated STA thread with timeout to prevent UI blocking from
        /// unresponsive shell extensions.
        /// </summary>
        private Task AppendShellExtensionItemsAsync(MenuFlyout menu, string path)
        {
            if (OwnerHwnd == IntPtr.Zero) return Task.CompletedTask;
            return AppendShellExtensionItemsCoreAsync(menu, path);
        }

        private async Task AppendShellExtensionItemsCoreAsync(MenuFlyout menu, string path)
        {
            try
            {
                // Dispose previous session
                _currentSession?.Dispose();
                _currentSession = await ShellContextMenu.CreateSessionAsync(OwnerHwnd, path);

                if (_currentSession == null || _currentSession.Items.Count == 0)
                    return;

                bool showDevMenu = _settings.ShowDeveloperMenu;
                bool showShellExtras = _settings.ShowWindowsShellExtras;
                bool showCopilot = _settings.ShowCopilotMenu;

                // Two-pass collection: track which items are "edit with X"
                var items = new List<(MenuFlyoutItemBase item, bool isEdit)>();

                foreach (var shellItem in _currentSession.Items)
                {
                    if (shellItem.IsSeparator)
                    {
                        items.Add((new MenuFlyoutSeparator(), false));
                        continue;
                    }

                    // Filter Copilot items when toggle is OFF
                    if (!showCopilot && IsCopilotItem(shellItem))
                        continue;

                    // Filter developer items when toggle is OFF
                    if (!showDevMenu && IsDeveloperItem(shellItem))
                        continue;

                    // Filter Windows shell extras when toggle is OFF
                    if (!showShellExtras && IsWindowsShellExtraItem(shellItem))
                        continue;

                    bool isEdit = IsEditWithItem(shellItem);
                    var converted = ConvertShellItem(shellItem);
                    if (converted != null)
                        items.Add((converted, isEdit));
                }

                // Group "edit with X" items into submenu when 2+
                var editEntries = items.Where(x => x.isEdit).Select(x => x.item).ToList();
                List<MenuFlyoutItemBase> filtered;

                if (editEntries.Count >= 2)
                {
                    var editSub = new MenuFlyoutSubItem
                    {
                        Text = _loc.Get("EditWith"),
                        Icon = new FontIcon { Glyph = "\uE70F", FontSize = 14 }
                    };
                    ApplyCompact(editSub);
                    foreach (var ei in editEntries)
                        editSub.Items.Add(ei);

                    // Replace first edit item with submenu, skip the rest
                    filtered = new List<MenuFlyoutItemBase>();
                    bool submenuInserted = false;
                    foreach (var (item, isEdit) in items)
                    {
                        if (isEdit)
                        {
                            if (!submenuInserted)
                            {
                                filtered.Add(editSub);
                                submenuInserted = true;
                            }
                            // skip — already inside submenu
                        }
                        else
                        {
                            filtered.Add(item);
                        }
                    }
                }
                else
                {
                    filtered = items.Select(x => x.item).ToList();
                }

                // Remove leading/trailing separators
                while (filtered.Count > 0 && filtered[0] is MenuFlyoutSeparator)
                    filtered.RemoveAt(0);
                while (filtered.Count > 0 && filtered[^1] is MenuFlyoutSeparator)
                    filtered.RemoveAt(filtered.Count - 1);

                // Remove consecutive separators
                for (int i = filtered.Count - 1; i > 0; i--)
                {
                    if (filtered[i] is MenuFlyoutSeparator && filtered[i - 1] is MenuFlyoutSeparator)
                        filtered.RemoveAt(i);
                }

                // Insert before the last 2 items (separator + Properties)
                // so shell items appear in the correct position
                if (filtered.Count > 0)
                {
                    int insertAt = Math.Max(0, menu.Items.Count - 2);
                    // Only add separator if previous item isn't already one
                    if (insertAt == 0 || !(menu.Items[insertAt - 1] is MenuFlyoutSeparator))
                    {
                        menu.Items.Insert(insertAt, new MenuFlyoutSeparator());
                        insertAt++;
                    }
                    foreach (var item in filtered)
                    {
                        menu.Items.Insert(insertAt, item);
                        insertAt++;
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] Shell extension enumeration error: {ex.Message}");
            }
        }

        /// <summary>Convert a ShellMenuItem to a WinUI MenuFlyoutItemBase, applying translations.</summary>
        private MenuFlyoutItemBase? ConvertShellItem(ShellMenuItem shellItem)
        {
            if (shellItem.IsSeparator)
                return new MenuFlyoutSeparator();

            string translatedText = TranslateShellText(shellItem);

            if (shellItem.HasSubmenu)
            {
                var subItem = new MenuFlyoutSubItem { Text = translatedText };
                ApplyCompact(subItem);
                foreach (var child in shellItem.Children!)
                {
                    var childItem = ConvertShellItem(child);
                    if (childItem != null)
                        subItem.Items.Add(childItem);
                }
                return subItem.Items.Count > 0 ? subItem : null;
            }

            if (string.IsNullOrWhiteSpace(translatedText))
                return null;

            var item = new MenuFlyoutItem
            {
                Text = translatedText,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24,
                IsEnabled = !shellItem.IsDisabled
            };

            // Capture commandId and session reference for the click handler
            int cmdId = shellItem.CommandId;
            var session = _currentSession;
            item.Click += (s, e) => session?.InvokeCommand(cmdId);

            return item;
        }

        /// <summary>
        /// Translate shell menu item text using verb-based or text-based translation tables.
        /// Respects current app language. English items stay as-is when language is English.
        /// Priority: verb translation > text translation > original text.
        /// </summary>
        private string TranslateShellText(ShellMenuItem shellItem)
        {
            var lang = _loc.Language;
            if (lang == "en") return shellItem.Text; // No translation needed

            // 1. Try verb-based translation (most reliable)
            if (!string.IsNullOrEmpty(shellItem.Verb) &&
                ShellVerbTranslations.TryGetValue(lang, out var verbDict) &&
                verbDict.TryGetValue(shellItem.Verb, out var verbTranslation))
            {
                return verbTranslation;
            }

            // 2. Try text-based translation (fallback for items without verbs)
            if (!string.IsNullOrWhiteSpace(shellItem.Text) &&
                ShellTextTranslations.TryGetValue(lang, out var textDict) &&
                textDict.TryGetValue(shellItem.Text, out var textTranslation))
            {
                return textTranslation;
            }

            // 3. Return original text
            return shellItem.Text;
        }

        /// <summary>
        /// Check if a shell menu item is an "edit with program" item
        /// (e.g. Edit with Photos, Edit with Paint, Edit with Notepad, Create with Designer).
        /// </summary>
        private static bool IsEditWithItem(ShellMenuItem item)
        {
            // Check verb first — "edit" verb is the standard Windows shell edit verb
            if (!string.IsNullOrEmpty(item.Verb) &&
                item.Verb.Equals("edit", StringComparison.OrdinalIgnoreCase))
                return true;

            var text = item.Text;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return EditWithTextPatterns.Any(pattern =>
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if a shell menu item is a Copilot item (Microsoft 365 Copilot, Ask Copilot, etc.).
        /// </summary>
        private static bool IsCopilotItem(ShellMenuItem item)
        {
            var text = item.Text;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return CopilotTextPatterns.Any(pattern =>
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if a shell menu item belongs to a developer tool (Git, VS, etc.).
        /// </summary>
        private static bool IsDeveloperItem(ShellMenuItem item)
        {
            var text = item.Text;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (DeveloperTextPatterns.Any(pattern =>
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Also check submenu children
            if (item.HasSubmenu)
            {
                return item.Children!.Any(child =>
                    !string.IsNullOrWhiteSpace(child.Text) &&
                    DeveloperTextPatterns.Any(pattern =>
                        child.Text.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
            }

            return false;
        }

        /// <summary>
        /// Check if a shell menu item is a Windows shell extra (Share, Include in library, Pin to Start, etc.).
        /// </summary>
        private static bool IsWindowsShellExtraItem(ShellMenuItem item)
        {
            // Check verb first
            if (!string.IsNullOrEmpty(item.Verb) && WindowsShellExtraVerbs.Contains(item.Verb))
                return true;

            // Check text
            var text = item.Text;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return WindowsShellExtraTexts.Any(pattern =>
                text.Equals(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private void OnMenuClosed(object? sender, object e)
        {
            try
            {
                // Dispose shell COM session when menu closes
                _currentSession?.Dispose();
                _currentSession = null;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] Session dispose error: {ex.Message}");
                _currentSession = null;
            }
            finally
            {
                if (sender is MenuFlyout flyout)
                    flyout.Closed -= OnMenuClosed;
            }
        }

        /// <summary>
        /// Build the "New" menu items (folder + common file types) for toolbar dropdown.
        /// </summary>
        public MenuFlyout BuildNewItemMenu(string folderPath, IContextMenuHost host)
        {
            var menu = new MenuFlyout();
            menu.Items.Add(CreateItem(_loc.Get("NewFolder"), "\uE8B7", () => host.PerformNewFolder(folderPath)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("NewTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Text Document.txt")));
            menu.Items.Add(CreateItem(_loc.Get("NewWordDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Document.docx")));
            menu.Items.Add(CreateItem(_loc.Get("NewExcelSpreadsheet"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Spreadsheet.xlsx")));
            menu.Items.Add(CreateItem(_loc.Get("NewPowerPoint"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Presentation.pptx")));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("NewBitmapImage"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Bitmap Image.bmp")));
            menu.Items.Add(CreateItem(_loc.Get("NewRichTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Rich Text Document.rtf")));
            menu.Items.Add(CreateItem(_loc.Get("NewZipArchive"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Compressed (zipped) Folder.zip")));
            return menu;
        }

        private void ShowProperties(FileSystemViewModel item)
        {
            if (FileSystemRouter.IsRemotePath(item.Path))
            {
                ShowRemotePropertiesDialog(item);
            }
            else
            {
                _shellService.ShowProperties(item.Path);
            }
        }

        private async void ShowRemotePropertiesDialog(FileSystemViewModel item)
        {
            var xamlRoot = XamlRootProvider?.Invoke();
            if (xamlRoot == null) return;

            var infoPanel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 8 };

            void AddRow(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var row = new Microsoft.UI.Xaml.Controls.StackPanel
                {
                    Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Spacing = 8
                };
                row.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = label,
                    Width = 80,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                row.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = value,
                    IsTextSelectionEnabled = true
                });
                infoPanel.Children.Add(row);
            }

            AddRow(_loc.Get("FileName") ?? "이름", item.Name);
            AddRow(_loc.Get("FileType") ?? "종류", item.FileType);
            if (item is FileViewModel)
                AddRow(_loc.Get("FileSize") ?? "크기", item.Size);
            AddRow(_loc.Get("DateModified") ?? "수정일", item.DateModified);
            AddRow(_loc.Get("FilePath") ?? "경로", item.Path);

            var dialog = new ContentDialog
            {
                Title = _loc.Get("Properties"),
                Content = infoPanel,
                CloseButtonText = _loc.Get("OK") ?? "확인",
                XamlRoot = xamlRoot
            };

            try { await dialog.ShowAsync(); }
            catch { /* ignore if another dialog is open */ }
        }

        private static readonly Microsoft.UI.Xaml.Thickness CompactPadding = new(10, 2, 10, 2);

        private static MenuFlyoutItem CreateItem(string text, string? glyph, Action action)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24
            };
            if (glyph != null)
            {
                item.Icon = new FontIcon { Glyph = glyph, FontSize = 14 };
            }
            item.Click += (s, e) => action();
            return item;
        }

        private static void ApplyCompact(MenuFlyoutSubItem sub)
        {
            sub.FontSize = 12;
            sub.Padding = CompactPadding;
            sub.MinHeight = 24;
        }

        private static ToggleMenuFlyoutItem CreateToggle(string text, bool isChecked, Action action)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = text,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24,
                IsChecked = isChecked
            };
            item.Click += (s, e) => action();
            return item;
        }
    }
}
