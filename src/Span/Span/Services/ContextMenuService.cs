using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    /// <summary>
    /// 컨텍스트 메뉴 구성 서비스. 파일/폴더/드라이브/즐겨찾기/빈 영역에 대한 MenuFlyout을 생성하고,
    /// Windows Shell 확장 메뉴 항목(ShellContextMenu)을 비동기 로드하여 통합한다.
    /// 다국어 번역(verb/text 기반), 개발자/Copilot/Shell Extras 필터링, Edit With 그룹핑을 지원.
    /// </summary>
    public class ContextMenuService
    {
        private readonly ShellService _shellService;
        private readonly LocalizationService _loc;
        private readonly SettingsService _settings;

        /// <summary>Current shell menu session (kept alive while menu is open)</summary>
        private ShellContextMenu.Session? _currentSession;

        /// <summary>현재 열려 있는 MenuFlyout 추적 (단독 키 AccessKey 지원용)</summary>
        private MenuFlyout? _activeFlyout;

        /// <summary>현재 열린 서브메뉴 추적 (서브메뉴 내 AccessKey 탐색용)</summary>
        private MenuFlyoutSubItem? _openedSubItem;

        /// <summary>HWND of the owner window (set by MainWindow)</summary>
        public IntPtr OwnerHwnd { get; set; }

        /// <summary>Callback invoked when a shell extension command fails. Set by MainWindow to show toast.</summary>
        public Action<string>? InvokeFailedCallback { get; set; }

        /// <summary>Callback invoked after a shell extension command completes to refresh the file list.</summary>
        public Action? ShellCommandExecutedCallback { get; set; }

        /// <summary>마지막 메뉴 빌드 컨텍스트 (셸 확장 재표시용)</summary>
        private FileSystemViewModel? _lastMenuTarget;
        private IContextMenuHost? _lastMenuHost;
        private Microsoft.UI.Xaml.FrameworkElement? _lastMenuElement;
        private Windows.Foundation.Point _lastMenuPosition;

        /// <summary>셸 확장 재표시를 위해 마지막 우클릭 컨텍스트를 저장한다.</summary>
        public void SetLastMenuContext(FileSystemViewModel target, IContextMenuHost host,
            Microsoft.UI.Xaml.FrameworkElement element, Windows.Foundation.Point position)
        {
            _lastMenuTarget = target;
            _lastMenuHost = host;
            _lastMenuElement = element;
            _lastMenuPosition = position;
        }

        /// <summary>셸 확장 포함으로 메뉴를 재빌드하여 같은 위치에 다시 표시한다.</summary>
        public async Task RebuildMenuWithShellExtensionsAsync()
        {
            if (_lastMenuTarget == null || _lastMenuHost == null || _lastMenuElement == null)
                return;

            // 현재 열린 메뉴 닫기
            _activeFlyout?.Hide();

            MenuFlyout flyout;
            if (_lastMenuTarget is FolderViewModel folder)
                flyout = await BuildFolderMenuAsync(folder, _lastMenuHost, forceShellExtensions: true);
            else if (_lastMenuTarget is FileViewModel file)
                flyout = await BuildFileMenuAsync(file, _lastMenuHost, forceShellExtensions: true);
            else
                return;

            flyout.ShowAt(_lastMenuElement, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = _lastMenuPosition
            });
        }

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

        public async Task<MenuFlyout> BuildFileMenuAsync(FileViewModel file, IContextMenuHost host, bool forceShellExtensions = false)
        {
            var menu = new MenuFlyout();
            bool isRemote = FileSystemRouter.IsRemotePath(file.Path);

            if (isRemote)
            {
                // ── Remote file menu (FTP/SFTP) ──
                menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(file.Path), "X"));
                menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(file.Path), "C"));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(file.Path, file.Name), "D"));
                menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(file), "M"));
                menu.Items.Add(new MenuFlyoutSeparator());

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path), "H"));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(file), "R"));
            }
            else
            {
                // ── Local file menu ──
                bool isArchive = Helpers.ArchivePathHelper.IsArchivePath(file.Path);

                menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(file), "O"));
                if (!isArchive)
                {
                    menu.Items.Add(CreateItem(_loc.Get("OpenWith"), "\uE7AC", () => _ = _shellService.OpenWithAsync(file.Path), "A"));
                }
                menu.Items.Add(new MenuFlyoutSeparator());

                if (!isArchive)
                {
                    menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(file.Path), "X"));
                }
                menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(file.Path), "C"));
                menu.Items.Add(new MenuFlyoutSeparator());

                if (!isArchive)
                {
                    // Compress / Extract
                    string ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ext == ".zip")
                    {
                        menu.Items.Add(CreateItem(_loc.Get("ExtractHere"), "\uE8B7", () => host.PerformExtractHere(file.Path), "E"));
                        menu.Items.Add(CreateItem(_loc.Get("ExtractTo"), "\uE8B7", () => host.PerformExtractTo(file.Path), "T"));
                        menu.Items.Add(new MenuFlyoutSeparator());
                    }
                    menu.Items.Add(CreateItem(_loc.Get("CompressToZip"), "\uE8C5", () => host.PerformCompress(new[] { file.Path }), "P"));
                    menu.Items.Add(new MenuFlyoutSeparator());

                    menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(file.Path, file.Name), "D"));
                    menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () =>
                    {
                        Helpers.DebugLogger.Log($"[Rename] Menu Click handler fired for '{file.Name}'");
                        try { host.PerformRename(file); }
                        catch (Exception ex) { Helpers.DebugLogger.Log($"[Rename] Menu Click EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
                    }, "M"));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(file.Path), "H"));
                if (!isArchive)
                {
                    menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(file.Path), "L"));
                }

                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(file), "R"));

                // Shell 항목: 캐시 히트 → 메뉴 표시 전 삽입 + 백그라운드 재검증
                //              캐시 미스 → await로 대기 후 삽입 (최대 1.5초)
                if (!isArchive)
                    await AppendShellExtensionItemsAsync(menu, file.Path, forceShellExtensions);
            }

            // Cleanup session when menu closes
            menu.Closed += OnMenuClosed;
            TrackFlyout(menu);

            return menu;
        }

        public async Task<MenuFlyout> BuildFolderMenuAsync(FolderViewModel folder, IContextMenuHost host, bool forceShellExtensions = false)
        {
            var menu = new MenuFlyout();
            bool isRemote = FileSystemRouter.IsRemotePath(folder.Path);
            bool isArchive = Helpers.ArchivePathHelper.IsArchivePath(folder.Path);

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpen(folder), "O"));
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!isArchive)
            {
                menu.Items.Add(CreateItem(_loc.Get("Cut"), "\uE8C6", () => host.PerformCut(folder.Path), "X"));
            }
            menu.Items.Add(CreateItem(_loc.Get("Copy"), "\uE8C8", () => host.PerformCopy(folder.Path), "C"));
            if (!isArchive)
            {
                var folderPaste = CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folder.Path), "V");
                folderPaste.IsEnabled = host.HasClipboardContent;
                menu.Items.Add(folderPaste);
            }
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!isRemote && !isArchive)
            {
                // Compress (local only)
                menu.Items.Add(CreateItem(_loc.Get("CompressToZip"), "\uE8C5", () => host.PerformCompress(new[] { folder.Path }), "P"));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            if (!isArchive)
            {
                menu.Items.Add(CreateItem(_loc.Get("Delete"), "\uE74D", () => host.PerformDelete(folder.Path, folder.Name), "D"));
                menu.Items.Add(CreateItem(_loc.Get("Rename"), "\uE70F", () => host.PerformRename(folder), "M"));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            if (!isRemote && !isArchive)
            {
                bool isFav = host.IsFavorite(folder.Path);
                if (isFav)
                    menu.Items.Add(CreateItem(_loc.Get("RemoveFromFavorites"), "\uE735", () => host.RemoveFromFavorites(folder.Path), "I"));
                else
                    menu.Items.Add(CreateItem(_loc.Get("AddToFavorites"), "\uE734", () => host.AddToFavorites(folder.Path), "I"));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(folder.Path), "H"));

            if (!isRemote && !isArchive)
            {
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(folder.Path), "L"));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => ShowProperties(folder), "R"));

            // Shell 항목: 캐시 히트 → 메뉴 표시 전 삽입 + 백그라운드 재검증
            //              캐시 미스 → await로 대기 후 삽입 (최대 1.5초)
            if (!isRemote && !isArchive)
                await AppendShellExtensionItemsAsync(menu, folder.Path, forceShellExtensions);

            menu.Closed += OnMenuClosed;
            TrackFlyout(menu);

            return menu;
        }

        public MenuFlyout BuildDriveMenu(DriveItem drive, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpenDrive(drive), "O"));

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
                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path), "H"));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("EditConnection"), "\uE70F", () =>
                {
                    if (!string.IsNullOrEmpty(drive.ConnectionId))
                        host.EditRemoteConnection(drive.ConnectionId);
                }, "E"));
                menu.Items.Add(CreateItem(_loc.Get("RemoveConnection"), "\uE74D", () =>
                {
                    if (!string.IsNullOrEmpty(drive.ConnectionId))
                        host.RemoveRemoteConnection(drive.ConnectionId);
                }, "M"));
            }
            else if (drive.IsCloudStorage)
            {
                // 클라우드 스토리지: 경로 복사 + 탐색기
                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path), "H"));
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(drive.Path), "L"));
            }
            else
            {
                // 로컬/리무버블/CD/네트워크 매핑 드라이브
                bool isRemovableOrCdrom = drive.DriveType == "Removable" || drive.DriveType == "CDRom";
                bool isNetwork = drive.DriveType == "Network";

                // 꺼내기 (Removable / CDRom)
                if (isRemovableOrCdrom)
                {
                    menu.Items.Add(CreateItem(_loc.Get("Eject"), "\uE7E7", () => host.PerformEjectDrive(drive), "J"));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }

                // 연결 끊기 (Network mapped)
                if (isNetwork)
                {
                    menu.Items.Add(CreateItem(_loc.Get("DisconnectDrive"), "\uE8CD", () => host.PerformDisconnectDrive(drive), "N"));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }

                menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(drive.Path), "H"));
                menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(drive.Path), "L"));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(drive.Path), "R"));
            }

            TrackFlyout(menu);
            return menu;
        }

        public MenuFlyout BuildFavoriteMenu(FavoriteItem fav, IContextMenuHost host)
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem(_loc.Get("Open"), "\uE8E5", () => host.PerformOpenFavorite(fav), "O"));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("RemoveFromFavorites"), "\uE735", () => host.RemoveFromFavorites(fav.Path), "I"));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("CopyPath"), "\uE8C8", () => _shellService.CopyPathToClipboard(fav.Path), "H"));
            menu.Items.Add(CreateItem(_loc.Get("OpenInExplorer"), "\uED25", () => _shellService.OpenInExplorer(fav.Path), "L"));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(CreateItem(_loc.Get("Properties"), "\uE946", () => _shellService.ShowProperties(fav.Path), "R"));

            TrackFlyout(menu);
            return menu;
        }

        public MenuFlyout BuildEmptyAreaMenu(string folderPath, IContextMenuHost host)
        {
            var menu = new MenuFlyout();
            bool isArchive = Helpers.ArchivePathHelper.IsArchivePath(folderPath);

            if (!isArchive)
            {
                // New submenu: folder + common file types
                var newSub = new MenuFlyoutSubItem { Text = _loc.Get("New"), Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 } };
                ApplyCompact(newSub, "W");
                newSub.Items.Add(CreateItem(_loc.Get("NewFolder"), "\uE8B7", () => host.PerformNewFolder(folderPath), "F"));
                newSub.Items.Add(new MenuFlyoutSeparator());
                newSub.Items.Add(CreateItem(_loc.Get("NewTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Text Document.txt"), "T"));
                newSub.Items.Add(CreateItem(_loc.Get("NewWordDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Document.docx"), "W"));
                newSub.Items.Add(CreateItem(_loc.Get("NewExcelSpreadsheet"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Spreadsheet.xlsx"), "E"));
                newSub.Items.Add(CreateItem(_loc.Get("NewPowerPoint"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Presentation.pptx"), "P"));
                newSub.Items.Add(new MenuFlyoutSeparator());
                newSub.Items.Add(CreateItem(_loc.Get("NewBitmapImage"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Bitmap Image.bmp"), "B"));
                newSub.Items.Add(CreateItem(_loc.Get("NewRichTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Rich Text Document.rtf"), "R"));
                newSub.Items.Add(CreateItem(_loc.Get("NewZipArchive"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Compressed (zipped) Folder.zip"), "Z"));
                menu.Items.Add(newSub);

                var emptyPaste = CreateItem(_loc.Get("Paste"), "\uE77F", () => host.PerformPaste(folderPath), "V");
                emptyPaste.IsEnabled = host.HasClipboardContent;
                menu.Items.Add(emptyPaste);
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // View submenu
            var viewSub = new MenuFlyoutSubItem { Text = _loc.Get("View"), Icon = new FontIcon { Glyph = "\uE8FD", FontSize = 14 } };
            ApplyCompact(viewSub, "O");
            viewSub.Items.Add(CreateItem(_loc.Get("MillerColumns"), "\uF0E2", () => host.SwitchViewMode(ViewMode.MillerColumns), "M"));
            viewSub.Items.Add(CreateItem(_loc.Get("Details"), "\uE8EF", () => host.SwitchViewMode(ViewMode.Details), "D"));
            viewSub.Items.Add(new MenuFlyoutSeparator());
            viewSub.Items.Add(CreateItem(_loc.Get("ExtraLargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconExtraLarge), "X"));
            viewSub.Items.Add(CreateItem(_loc.Get("LargeIcons"), null, () => host.SwitchViewMode(ViewMode.IconLarge), "L"));
            viewSub.Items.Add(CreateItem(_loc.Get("MediumIcons"), null, () => host.SwitchViewMode(ViewMode.IconMedium), "E"));
            viewSub.Items.Add(CreateItem(_loc.Get("SmallIcons"), null, () => host.SwitchViewMode(ViewMode.IconSmall), "S"));
            menu.Items.Add(viewSub);

            // Sort submenu
            var sortSub = new MenuFlyoutSubItem { Text = _loc.Get("Sort"), Icon = new FontIcon { Glyph = "\uE8CB", FontSize = 14 } };
            ApplyCompact(sortSub, "D");
            sortSub.Items.Add(CreateItem(_loc.Get("Name"), "\uE8C1", () => host.ApplySort("Name"), "N"));
            sortSub.Items.Add(CreateItem(_loc.Get("Date"), "\uE787", () => host.ApplySort("Date"), "D"));
            sortSub.Items.Add(CreateItem(_loc.Get("Size"), "\uE91B", () => host.ApplySort("Size"), "S"));
            sortSub.Items.Add(CreateItem(_loc.Get("Type"), "\uE8FD", () => host.ApplySort("Type"), "T"));
            sortSub.Items.Add(new MenuFlyoutSeparator());
            sortSub.Items.Add(CreateItem(_loc.Get("Ascending"), "\uE74A", () => host.ApplySortDirection(true), "A"));
            sortSub.Items.Add(CreateItem(_loc.Get("Descending"), "\uE74B", () => host.ApplySortDirection(false), "E"));
            menu.Items.Add(sortSub);

            // Group By submenu
            var currentGroup = host.CurrentGroupBy;
            var groupSub = new MenuFlyoutSubItem { Text = _loc.Get("GroupBy"), Icon = new FontIcon { Glyph = "\uF168", FontSize = 14 } };
            ApplyCompact(groupSub, "G");
            groupSub.Items.Add(CreateToggle(_loc.Get("None"), currentGroup == "None", () => host.ApplyGroupBy("None"), "O"));
            groupSub.Items.Add(CreateToggle(_loc.Get("Name"), currentGroup == "Name", () => host.ApplyGroupBy("Name"), "N"));
            groupSub.Items.Add(CreateToggle(_loc.Get("Type"), currentGroup == "Type", () => host.ApplyGroupBy("Type"), "T"));
            groupSub.Items.Add(CreateToggle(_loc.Get("Date"), currentGroup == "DateModified", () => host.ApplyGroupBy("DateModified"), "D"));
            groupSub.Items.Add(CreateToggle(_loc.Get("Size"), currentGroup == "Size", () => host.ApplyGroupBy("Size"), "S"));
            menu.Items.Add(groupSub);

            menu.Items.Add(new MenuFlyoutSeparator());

            // Selection submenu
            var selectSub = new MenuFlyoutSubItem { Text = _loc.Get("Select"), Icon = new FontIcon { Glyph = "\uE762", FontSize = 14 } };
            ApplyCompact(selectSub, "S");
            selectSub.Items.Add(CreateItem(_loc.Get("SelectAll") + "  Ctrl+A", "\uE8B3", () => host.PerformSelectAll(), "A"));
            selectSub.Items.Add(CreateItem(_loc.Get("SelectNone") + "  Ctrl+Shift+A", null, () => host.PerformSelectNone(), "N"));
            selectSub.Items.Add(CreateItem(_loc.Get("InvertSelection") + "  Ctrl+I", null, () => host.PerformInvertSelection(), "I"));
            menu.Items.Add(selectSub);

            TrackFlyout(menu);
            return menu;
        }

        /// <summary>
        /// Shell 확장 항목을 메뉴에 추가 (await로 대기, 최대 1.5초 타임아웃).
        /// </summary>
        private Task AppendShellExtensionItemsAsync(MenuFlyout menu, string path, bool forceLoad = false)
        {
            if (OwnerHwnd == IntPtr.Zero) return Task.CompletedTask;

            // ShowShellExtensions OFF + forceLoad가 아닌 경우 → "셸 확장 항목 표시" 메뉴만 추가
            if (!forceLoad && !_settings.ShowShellExtensions)
            {
                var showShellItem = CreateItem(_loc.Get("Shell_ShowExtensions"), "\uE8B7", async () =>
                {
                    await RebuildMenuWithShellExtensionsAsync();
                });
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(showShellItem);
                return Task.CompletedTask;
            }

            return AppendShellExtensionItemsCoreAsync(menu, path);
        }

        private async Task AppendShellExtensionItemsCoreAsync(MenuFlyout menu, string path)
        {
            // Insert loading indicator before Properties (last 2 items = separator + Properties)
            int loadingIdx = Math.Max(0, menu.Items.Count - 2);
            var loadingSep = new MenuFlyoutSeparator();
            var loadingItem = new MenuFlyoutItem
            {
                Text = _loc.Get("Shell_Loading"),
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24,
                IsEnabled = false,
                Icon = new FontIcon { Glyph = "\uE117", FontSize = 14 }  // Sync glyph as loading indicator
            };
            menu.Items.Insert(loadingIdx, loadingSep);
            menu.Items.Insert(loadingIdx + 1, loadingItem);

            try
            {
                // 이전 세션 정리
                _currentSession?.Dispose();
                _currentSession = null;

                Helpers.DebugLogger.Log($"[ContextMenuService] Shell CreateSessionAsync START: {path}");
                _currentSession = await ShellContextMenu.CreateSessionAsync(OwnerHwnd, path);
                Helpers.DebugLogger.Log($"[ContextMenuService] Shell CreateSessionAsync END: items={_currentSession?.Items.Count ?? 0}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] Shell extension error: {ex.GetType().Name}: {ex.Message}");
                Helpers.DebugLogger.Log($"[ContextMenuService] Shell extension StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                    Helpers.DebugLogger.Log($"[ContextMenuService] Shell extension Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            finally
            {
                // Remove loading indicator (menu may have been closed during await)
                try
                {
                    menu.Items.Remove(loadingItem);
                    menu.Items.Remove(loadingSep);
                }
                catch { }
            }

            if (_currentSession == null || _currentSession.Items.Count == 0)
                return;

            InsertShellItemsToMenu(menu, _currentSession.Items);
            Helpers.DebugLogger.Log($"[ContextMenuService] Shell items inserted: menu.Items.Count={menu.Items.Count}");
        }

        /// <summary>Shell 항목 목록을 필터링/그룹핑하여 메뉴에 삽입</summary>
        private void InsertShellItemsToMenu(MenuFlyout menu, IList<ShellMenuItem> shellItems)
        {
            bool showDevMenu = _settings.ShowDeveloperMenu;
            bool showShellExtras = _settings.ShowWindowsShellExtras;
            bool showCopilot = _settings.ShowCopilotMenu;

            var items = new List<(MenuFlyoutItemBase item, bool isEdit)>();

            Helpers.DebugLogger.Log($"[ShellEnum] Raw shell items: {shellItems.Count}");
            for (int si = 0; si < shellItems.Count; si++)
            {
                var s = shellItems[si];
                Helpers.DebugLogger.Log($"[ShellEnum] [{si}] text='{s.Text}' verb='{s.Verb}' sep={s.IsSeparator} ownerDraw={s.IsOwnerDrawn} disabled={s.IsDisabled} sub={s.HasSubmenu} accel='{s.Accelerator}' cmdId={s.CommandId}");
            }

            foreach (var shellItem in shellItems)
            {
                try
                {
                    if (shellItem.IsSeparator)
                    {
                        items.Add((new MenuFlyoutSeparator(), false));
                        continue;
                    }

                    // OwnerDrawn 항목: 텍스트도 verb도 없으면 WinUI로 표현 불가 → 스킵
                    // 반디집, 7-Zip 등은 OwnerDrawn이면서 텍스트를 설정하므로 통과시킨다.
                    if (shellItem.IsOwnerDrawn && string.IsNullOrWhiteSpace(shellItem.Text)
                        && string.IsNullOrEmpty(shellItem.Verb))
                        continue;

                    if (!showCopilot && IsCopilotItem(shellItem)) continue;
                    if (!showDevMenu && IsDeveloperItem(shellItem)) continue;
                    if (!showShellExtras && IsWindowsShellExtraItem(shellItem)) continue;

                    bool isEdit = IsEditWithItem(shellItem);
                    var converted = ConvertShellItem(shellItem);
                    if (converted != null)
                        items.Add((converted, isEdit));
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ContextMenuService] ConvertShellItem failed: text='{shellItem.Text}' verb='{shellItem.Verb}' ownerDrawn={shellItem.IsOwnerDrawn} err={ex.Message}");
                }
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
                foreach (var ei in editEntries) editSub.Items.Add(ei);

                filtered = new List<MenuFlyoutItemBase>();
                bool submenuInserted = false;
                foreach (var (item, isEdit) in items)
                {
                    if (isEdit)
                    {
                        if (!submenuInserted) { filtered.Add(editSub); submenuInserted = true; }
                    }
                    else filtered.Add(item);
                }
            }
            else
            {
                filtered = items.Select(x => x.item).ToList();
            }

            // Clean separators
            while (filtered.Count > 0 && filtered[0] is MenuFlyoutSeparator) filtered.RemoveAt(0);
            while (filtered.Count > 0 && filtered[^1] is MenuFlyoutSeparator) filtered.RemoveAt(filtered.Count - 1);
            for (int i = filtered.Count - 1; i > 0; i--)
            {
                if (filtered[i] is MenuFlyoutSeparator && filtered[i - 1] is MenuFlyoutSeparator)
                    filtered.RemoveAt(i);
            }

            if (filtered.Count == 0) return;

            // Insert before the last 2 items (separator + Properties)
            int insertAt = Math.Max(0, menu.Items.Count - 2);

            if (insertAt == 0 || !(menu.Items[insertAt - 1] is MenuFlyoutSeparator))
            {
                menu.Items.Insert(insertAt, new MenuFlyoutSeparator());
                insertAt++;
            }

            Helpers.DebugLogger.Log($"[ShellInsert] Inserting {filtered.Count} items at pos={insertAt}");
            for (int idx = 0; idx < filtered.Count; idx++)
            {
                var item = filtered[idx];
                string desc = item switch
                {
                    MenuFlyoutSeparator => "---separator---",
                    MenuFlyoutSubItem sub => $"SubItem(text='{sub.Text}' children={sub.Items.Count})",
                    MenuFlyoutItem mfi => $"Item(text='{mfi.Text}' enabled={mfi.IsEnabled})",
                    _ => item.GetType().Name
                };
                try
                {
                    Helpers.DebugLogger.Log($"[ShellInsert] [{idx}] {desc}");
                    menu.Items.Insert(insertAt, item);
                    Helpers.DebugLogger.Log($"[ShellInsert] [{idx}] inserted OK");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ShellInsert] [{idx}] INSERT FAILED: {ex.GetType().Name}: {ex.Message}");
                }
                insertAt++;
            }
        }

        /// <summary>Convert a ShellMenuItem to a WinUI MenuFlyoutItemBase, applying translations.</summary>
        private MenuFlyoutItemBase? ConvertShellItem(ShellMenuItem shellItem)
        {
            if (shellItem.IsSeparator)
                return new MenuFlyoutSeparator();

            string translatedText = SanitizeMenuText(TranslateShellText(shellItem));
            Helpers.DebugLogger.Log($"[ConvertShell] text='{shellItem.Text}' → '{translatedText}' verb='{shellItem.Verb}' sub={shellItem.HasSubmenu} cmdId={shellItem.CommandId}");

            if (shellItem.HasSubmenu)
            {
                // 빈 텍스트 SubItem 방어 (OwnerDrawn 항목 등)
                if (string.IsNullOrWhiteSpace(translatedText))
                    translatedText = shellItem.Verb ?? "...";
                Helpers.DebugLogger.Log($"[ConvertShell] Creating SubItem text='{translatedText}'");
                var subItem = new MenuFlyoutSubItem { Text = translatedText };
                ApplyCompact(subItem);
                // Shell SubItem: WinUI AccessKey 대신 Tag에 accelerator 저장
                // (WinUI AccessKey는 일부 Shell accelerator 값에서 E_INVALIDARG 발생)
                if (!string.IsNullOrEmpty(shellItem.Accelerator))
                    subItem.Tag = shellItem.Accelerator;
                foreach (var child in shellItem.Children!)
                {
                    var childItem = ConvertShellItem(child);
                    if (childItem != null)
                        subItem.Items.Add(childItem);
                }
                Helpers.DebugLogger.Log($"[ConvertShell] SubItem done children={subItem.Items.Count}");
                return subItem.Items.Count > 0 ? subItem : null;
            }

            if (string.IsNullOrWhiteSpace(translatedText))
                return null;

            // 세션 참조와 CommandId 캡처
            int cmdId = shellItem.CommandId;
            var session = _currentSession;
            Action invokeAction = () =>
            {
                var success = session?.InvokeCommand(cmdId) ?? false;
                if (!success)
                {
                    InvokeFailedCallback?.Invoke(translatedText);
                }
            };

            Helpers.DebugLogger.Log($"[ConvertShell] Creating MenuFlyoutItem text='{translatedText}' cmdId={cmdId}");
            var item = new MenuFlyoutItem
            {
                Text = translatedText,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24,
                IsEnabled = !shellItem.IsDisabled,
                Tag = invokeAction // TryInvokeAccessKey에서 사용
            };

            item.Click += (s, e) =>
            {
                try { invokeAction(); }
                finally { ShellCommandExecutedCallback?.Invoke(); }
            };
            Helpers.DebugLogger.Log($"[ConvertShell] MenuFlyoutItem created OK");

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
                _currentSession?.Dispose();
                _currentSession = null;
                _openedSubItem = null;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] Session dispose error: {ex.Message}");
                _currentSession = null;
            }
            finally
            {
                if (sender is MenuFlyout flyout)
                {
                    flyout.Closed -= OnMenuClosed;
                    if (_activeFlyout == flyout) _activeFlyout = null;
                }
            }
        }

        /// <summary>
        /// 활성 MenuFlyout을 추적하여 단독 키 AccessKey를 지원.
        /// Build*Menu 메서드에서 자동 호출.
        /// </summary>
        private void TrackFlyout(MenuFlyout flyout)
        {
            _activeFlyout = flyout;
            _openedSubItem = null;
            flyout.Closed += (s, e) =>
            {
                if (_activeFlyout == flyout)
                {
                    _activeFlyout = null;
                    _openedSubItem = null;
                }
            };
        }

        /// <summary>
        /// 현재 열린 MenuFlyout에서 AccessKey가 일치하는 항목을 찾아 실행.
        /// WinUI 3의 AccessKey는 Alt 키가 필요하므로, 단독 키 입력을 직접 처리한다.
        /// top-level 항목을 우선 검색하고, 없으면 서브메뉴 내부까지 재귀 탐색한다.
        /// </summary>
        /// <returns>AccessKey 일치 항목이 있으면 true (실행 완료)</returns>
        public bool TryInvokeAccessKey(string key)
        {
            if (_activeFlyout == null) return false;

            try
            {
                // 서브메뉴가 열려 있으면 해당 서브메뉴의 Items만 탐색
                // → top-level "새로만들기(W)"와 서브메뉴 내 "Word(W)" 충돌 방지
                if (_openedSubItem != null)
                {
                    if (TryInvokeInItems(_openedSubItem.Items, key, recursive: false))
                        return true;
                    if (TryInvokeInItems(_openedSubItem.Items, key, recursive: true))
                        return true;
                    // 서브메뉴 내에서 매칭 못 하면 WinUI에 위임 (e.Handled = false)
                    return false;
                }

                // 1차: top-level 항목 검색 (MenuFlyoutItem, ToggleMenuFlyoutItem, MenuFlyoutSubItem)
                if (TryInvokeInItems(_activeFlyout.Items, key, recursive: false))
                    return true;

                // 2차: 서브메뉴 내부 재귀 검색
                if (TryInvokeInItems(_activeFlyout.Items, key, recursive: true))
                    return true;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenuService] TryInvokeAccessKey error: {ex.Message}");
            }

            return false;
        }

        private bool TryInvokeInItems(IList<MenuFlyoutItemBase> items, string key, bool recursive)
        {
            foreach (var item in items)
            {
                // MenuFlyoutItem: 텍스트 suffix "(X)" 또는 Tag 매칭
                if (item is MenuFlyoutItem mfi && mfi.Tag is Action action)
                {
                    bool matches = ExtractAccessKeyFromText(mfi.Text)
                        ?.Equals(key, StringComparison.OrdinalIgnoreCase) == true;
                    if (matches)
                    {
                        _activeFlyout?.Hide();
                        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                        if (dq != null) dq.TryEnqueue(() => action());
                        else action();
                        return true;
                    }
                }

                if (item is ToggleMenuFlyoutItem tmfi
                    && tmfi.Tag is Action toggleAction
                    && ExtractAccessKeyFromText(tmfi.Text)
                        ?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _activeFlyout?.Hide();
                    var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    if (dq != null) dq.TryEnqueue(() => toggleAction());
                    else toggleAction();
                    return true;
                }

                // SubItem: 텍스트 suffix "(X)" 또는 Tag에 저장된 accelerator 매칭
                if (item is MenuFlyoutSubItem sub)
                {
                    string? subKey = sub.Tag is string tagKey ? tagKey
                        : ExtractAccessKeyFromText(sub.Text);
                    if (!string.IsNullOrEmpty(subKey) && subKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        // 서브메뉴가 열린 것으로 추적 → 이후 키 입력은 서브메뉴 Items만 탐색
                        _openedSubItem = sub;
                        // 서브메뉴 열기: Focus 후 다음 프레임에서 Right Arrow 키 시뮬레이션
                        try { sub.Focus(Microsoft.UI.Xaml.FocusState.Keyboard); } catch { }
                        sub.DispatcherQueue?.TryEnqueue(
                            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                            {
                                const byte VK_RIGHT = 0x27;
                                const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
                                const uint KEYEVENTF_KEYUP = 0x0002;
                                Helpers.NativeMethods.keybd_event(VK_RIGHT, 0, KEYEVENTF_EXTENDEDKEY, 0);
                                Helpers.NativeMethods.keybd_event(VK_RIGHT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
                            });
                        return true;
                    }
                }

                // 재귀 모드일 때만 서브메뉴 내부 탐색
                if (recursive && item is MenuFlyoutSubItem recSub && recSub.Items.Count > 0)
                {
                    if (TryInvokeInItems(recSub.Items, key, recursive: true))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Build the "New" menu items (folder + common file types) for toolbar dropdown.
        /// </summary>
        public MenuFlyout BuildNewItemMenu(string folderPath, IContextMenuHost host)
        {
            if (Helpers.ArchivePathHelper.IsArchivePath(folderPath)) return new MenuFlyout();
            var menu = new MenuFlyout();
            menu.Items.Add(CreateItem(_loc.Get("NewFolder"), "\uE8B7", () => host.PerformNewFolder(folderPath), "F"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("NewTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Text Document.txt"), "T"));
            menu.Items.Add(CreateItem(_loc.Get("NewWordDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Document.docx"), "W"));
            menu.Items.Add(CreateItem(_loc.Get("NewExcelSpreadsheet"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Spreadsheet.xlsx"), "E"));
            menu.Items.Add(CreateItem(_loc.Get("NewPowerPoint"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Presentation.pptx"), "P"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem(_loc.Get("NewBitmapImage"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Bitmap Image.bmp"), "B"));
            menu.Items.Add(CreateItem(_loc.Get("NewRichTextDocument"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Rich Text Document.rtf"), "R"));
            menu.Items.Add(CreateItem(_loc.Get("NewZipArchive"), "\uE8A5", () => host.PerformNewFile(folderPath, "New Compressed (zipped) Folder.zip"), "Z"));
            TrackFlyout(menu);
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
            try
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
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] ShowRemotePropertiesDialog error: {ex.Message}");
            }
        }

        /// <summary>
        /// Shell 메뉴 텍스트 세정: 탭 문자(accelerator 구분자), 제어문자 제거.
        /// Win32 HMENU 텍스트에 포함된 \t 이후 문자열은 accelerator 힌트이므로 제거.
        /// WinUI MenuFlyoutItem.Text에 탭/제어문자가 있으면 E_INVALIDARG 발생 가능.
        /// </summary>
        private static string SanitizeMenuText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Win32 메뉴는 \t 이후를 accelerator 표시용으로 사용 (예: "Undo\tCtrl+Z")
            int tabIdx = text.IndexOf('\t');
            if (tabIdx >= 0) text = text.Substring(0, tabIdx);
            // 제어문자 제거 (U+0000~U+001F, U+007F)
            if (text.Any(c => char.IsControl(c)))
                text = new string(text.Where(c => !char.IsControl(c)).ToArray());
            return text;
        }

        /// <summary>
        /// 메뉴 텍스트에서 AccessKey 추출: "Open(O)" → "O", "복사(C)" → "C"
        /// 텍스트 끝이 "(X)" 형식일 때 X를 반환, 아니면 null.
        /// </summary>
        private static string? ExtractAccessKeyFromText(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 3) return null;
            if (text[^1] != ')') return null;
            int openParen = text.LastIndexOf('(');
            if (openParen < 0 || openParen >= text.Length - 2) return null;
            string key = text.Substring(openParen + 1, text.Length - openParen - 2);
            return key.Length == 1 ? key : null;
        }

        private static readonly Microsoft.UI.Xaml.Thickness CompactPadding = new(10, 2, 10, 2);

        private static MenuFlyoutItem CreateItem(string text, string? glyph, Action action, string? accessKey = null)
        {
            var item = new MenuFlyoutItem
            {
                Text = accessKey != null ? $"{text}({accessKey})" : text,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24
            };
            if (glyph != null)
            {
                item.Icon = new FontIcon { Glyph = glyph, FontSize = 14 };
            }
            if (accessKey != null)
            {
                // WinUI AccessKey 속성 사용 금지: visual tree 미연결 상태에서
                // AccessKey 설정 시 E_INVALIDARG 발생 (네이티브 XAML 예외, try-catch 불가).
                // 대신 Tag에 action 저장 → TryInvokeAccessKey에서 단축키 처리.
                item.Tag = action;
            }
            item.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[ContextMenu] Click handler exception: {ex.GetType().Name}: {ex.Message}"); }
            };
            return item;
        }

        private static void ApplyCompact(MenuFlyoutSubItem sub, string? accessKey = null)
        {
            sub.FontSize = 12;
            sub.Padding = CompactPadding;
            sub.MinHeight = 24;
            if (accessKey != null)
            {
                // 이미 (X) suffix가 있으면 추가하지 않음 (CJK Shell 메뉴 중복 방지)
                string suffix = $"({accessKey})";
                if (!sub.Text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    sub.Text = $"{sub.Text}({accessKey})";
                // AccessKey 속성 미설정 — Tag에 accelerator 저장으로 대체
                sub.Tag = accessKey;
            }
        }

        private static ToggleMenuFlyoutItem CreateToggle(string text, bool isChecked, Action action, string? accessKey = null)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = accessKey != null ? $"{text}({accessKey})" : text,
                FontSize = 12,
                Padding = CompactPadding,
                MinHeight = 24,
                IsChecked = isChecked
            };
            if (accessKey != null)
            {
                item.Tag = action;
            }
            item.Click += (s, e) => action();
            return item;
        }
    }
}
