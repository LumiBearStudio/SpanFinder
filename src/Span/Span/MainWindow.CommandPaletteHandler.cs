// ═══════════════════════════════════════════════════════════════════════════════
//  Command Palette (Ctrl+K) — 현재 숨김 처리 상태 (2026-04-10)
// ═══════════════════════════════════════════════════════════════════════════════
//
//  ▣ 현재 상태
//    - 코드는 전부 살아 있고 컴파일됨 (90+ 명령, 한글 초성 검색, 컨텍스트 활성화 등)
//    - Services/KeyBindingService.cs 에서 기본 단축키 매핑(Ctrl+K)을 주석 처리해
//      사용자에게 노출되지 않음
//    - MainWindow.xaml 의 CommandPaletteOverlay 는 그대로 유지(Visibility=Collapsed)
//    - 어떤 UI 진입점도 없으므로 일반 사용자는 이 기능의 존재를 알 수 없음
//
//  ▣ 왜 숨겼나? (2026-04-10 의사결정)
//    Span 같은 파일 탐색기에서 IDE 스타일 Command Palette 가 가치를 더하는지
//    팀 분석을 진행한 결과, 다음 결론에 도달함:
//
//    1. 시장 증거 — 11개 주요 파일 탐색기 (Files App, Directory Opus, Total Commander,
//       XYplorer, Far Manager, Multi Commander, Q-Dir, Explorer++, Finder, Nautilus,
//       Dolphin) 중 정식 채택은 Files App 1개뿐. 30년된 파워유저 제품(Directory Opus,
//       XYplorer)은 요청을 받고도 거절함.
//
//    2. IDE 성공 조건 5개 중 파일 탐색기에 성립하는 것이 사실상 0개:
//       - 수천 개 명령 ✗ (Span 90개)
//       - 키보드 100% 워크플로우 ✗ (드래그/더블클릭 위주)
//       - 동작 중심 명령 ✗ ("어디로 이동"이 더 빈번)
//       - 깊은 메뉴 구조 ✗ (얕고 컨텍스트 메뉴가 충분)
//       - 텍스트가 1차 객체 ✗ (공간/시각적 객체가 1차)
//
//    3. 중복도 65~75% — 90개 명령 중 50개(Settings 토글/선택/섹션)가 SettingsModeView
//       와 100% 중복. 나머지는 단축키와 컨텍스트 메뉴로 이미 접근 가능.
//
//    4. Nielsen 6번째 휴리스틱(Recognition over Recall) 위반 — Command Palette 는
//       회상 패턴이고, 파일 탐색기 일반 사용자에게는 부적합.
//
//    5. 제작자 본인의 사용성 직감: "있어도 잘 안 쓸 것 같고 편할 것 같지 않다"
//
//    삭제하지 않고 숨김 처리만 한 이유: 한글 초성 검색, 컨텍스트 활성화, 토스트
//    피드백, 다국어 로컬라이즈 등 재사용 가치가 있는 자산이 많고, 향후 "Quick Open"
//    (Sublime/Files App Omnibar 패턴 — 폴더 이동 중심) 으로 재설계할 가능성을 열어둠.
//
//  ▣ 다시 활성화하려면 (개발자용)
//    1. Services/KeyBindingService.cs 에서
//         // [ShortcutCommands.OpenCommandPalette] = ["Ctrl+K"],
//       라인의 주석을 해제
//    2. 빌드 후 Ctrl+K 로 즉시 사용 가능 (앱 재시작 시 키 바인딩 재로드)
//    3. 사용자 설정에 이미 키가 저장되어 있으면 Settings → Shortcuts 에서 리셋 필요
//
//  ▣ 향후 재설계 방향 (아이디어)
//    Command Palette → Quick Open 으로 리프레이밍:
//      - 기본 모드  : 폴더로 이동 (최근/즐겨찾기/Known Folders/탭/드라이브)
//      - "> " 접두어: 명령 모드 (현재 90개 → 40개로 축소)
//      - "? " 접두어: 설정 검색
//      - "/ " 접두어: 현재 폴더에서 파일 검색
//    Files App Omnibar 패턴 + Sublime "Go To Anything" + 한글 초성 검색의 결합.
//    재설계 시 이 파일과 BuildCommandCatalog() 를 출발점으로 삼을 것.
//
//  ▣ 관련 파일
//    - Models/ShortcutCommands.cs                : OpenCommandPalette 등 90+ 상수
//    - Models/CommandPaletteItem.cs              : 데이터 모델
//    - Helpers/HangulSearchHelper.cs             : 한글 초성 검색 (재사용 가치 높음)
//    - MainWindow.xaml (1827~1901행)             : Overlay UI (숨김 상태)
//    - MainWindow.KeyboardHandler.cs             : ExecuteCommand의 case 분기
//    - Services/KeyBindingService.cs             : 단축키 기본 매핑 (현재 비활성)
//    - Services/LocalizationData.cs              : Cmd_*, CommandPalette_* 키 90+
//
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Helpers;
using Span.Models;
using Span.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span
{
    /// <summary>
    /// Command Palette (Ctrl+K) 관련 이벤트 핸들러.
    /// **현재 숨김 처리됨** — 파일 상단의 큰 주석 블록 참조.
    /// 한글 검색, 컨텍스트 기반 비활성화, Settings 통합, 카테고리 그룹화, 최근 사용 추적 지원.
    /// </summary>
    public partial class MainWindow
    {
        private bool _isCommandPaletteOpen;
        private List<CommandPaletteItem>? _commandCatalog;
        private const int MaxRecentCommands = 8;

        internal void ToggleCommandPalette()
        {
            if (_isCommandPaletteOpen) CloseCommandPalette();
            else OpenCommandPalette();
        }

        private void OpenCommandPalette()
        {
            _isCommandPaletteOpen = true;
            CommandPaletteOverlay.Visibility = Visibility.Visible;
            CommandPaletteInput.Text = string.Empty;
            CommandPaletteInput.PlaceholderText = _loc.Get("CommandPalette_Placeholder");
            CommandPaletteInput.Focus(FocusState.Programmatic);

            _commandCatalog = BuildCommandCatalog();
            UpdateCommandPaletteResults(string.Empty);
        }

        private void CloseCommandPalette()
        {
            _isCommandPaletteOpen = false;
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnCommandPaletteOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            CloseCommandPalette();
        }

        private void OnCommandPaletteInputTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCommandPaletteResults(CommandPaletteInput.Text);
        }

        private void OnCommandPaletteInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseCommandPalette();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ExecuteSelectedPaletteItem();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                if (CommandPaletteList.Items.Count > 0)
                {
                    var idx = CommandPaletteList.SelectedIndex;
                    CommandPaletteList.SelectedIndex = Math.Min(idx + 1, CommandPaletteList.Items.Count - 1);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Up)
            {
                if (CommandPaletteList.Items.Count > 0)
                {
                    var idx = CommandPaletteList.SelectedIndex;
                    CommandPaletteList.SelectedIndex = Math.Max(idx - 1, 0);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void OnCommandPaletteItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CommandPaletteItem item)
                ExecutePaletteItem(item);
        }

        private void ExecuteSelectedPaletteItem()
        {
            if (CommandPaletteList.SelectedItem is CommandPaletteItem item)
                ExecutePaletteItem(item);
        }

        private void ExecutePaletteItem(CommandPaletteItem item)
        {
            // 비활성 항목은 무시 (회색 처리된 명령)
            if (!item.IsEnabled)
            {
                ViewModel.ShowToast(_loc.Get("CommandPalette_NotAvailable"));
                return;
            }

            CloseCommandPalette();

            switch (item.Type)
            {
                case CommandPaletteItemType.Command:
                case CommandPaletteItemType.SettingToggle:
                case CommandPaletteItemType.SettingSelect:
                case CommandPaletteItemType.SettingsSection:
                    if (!string.IsNullOrEmpty(item.CommandId))
                    {
                        TrackRecentCommand(item.CommandId);
                        ExecuteCommand(item.CommandId);
                    }
                    break;

                case CommandPaletteItemType.Tab:
                    if (item.TabIndex >= 0 && item.TabIndex < ViewModel.Tabs.Count)
                        SwitchToTabByIndex(item.TabIndex);
                    break;

                case CommandPaletteItemType.Navigation:
                    break;
            }
        }

        // ── 결과 갱신 ───────────────────────────────────────────

        private void UpdateCommandPaletteResults(string query)
        {
            if (_commandCatalog == null) return;

            // 컨텍스트 변경에 따라 IsEnabled 매번 재평가
            foreach (var item in _commandCatalog)
                item.IsEnabled = IsCommandAvailable(item);

            List<CommandPaletteItem> results;

            if (string.IsNullOrWhiteSpace(query))
            {
                // 빈 입력: 최근 사용 → 그 외 알파벳 순
                var recentIds = LoadRecentCommandIds();
                var recentItems = recentIds
                    .Select(id => _commandCatalog.FirstOrDefault(c => c.CommandId == id))
                    .Where(c => c != null)
                    .Cast<CommandPaletteItem>()
                    .ToList();

                var rest = _commandCatalog
                    .Where(c => !recentIds.Contains(c.CommandId))
                    .OrderByDescending(c => c.IsEnabled) // 활성 우선
                    .ThenBy(c => c.Category)
                    .ThenBy(c => c.Title)
                    .Take(60)
                    .ToList();

                results = recentItems.Concat(rest).ToList();
            }
            else
            {
                // 검색: 한글/초성/영문 매칭
                results = _commandCatalog
                    .Where(c => MatchesQuery(c, query))
                    .OrderByDescending(c => c.IsEnabled)
                    .ThenByDescending(c => ScoreItem(c, query))
                    .Take(80)
                    .ToList();
            }

            CommandPaletteList.ItemsSource = results;
            if (results.Count > 0)
                CommandPaletteList.SelectedIndex = 0;

            CommandPaletteNoResults.Visibility = results.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool MatchesQuery(CommandPaletteItem item, string query)
        {
            if (HangulSearchHelper.Match(item.Title, query)) return true;
            if (HangulSearchHelper.Match(item.Category, query)) return true;
            if (HangulSearchHelper.Match(item.GroupName, query)) return true;
            foreach (var alias in item.Aliases)
            {
                if (HangulSearchHelper.Match(alias, query)) return true;
            }
            return false;
        }

        private static int ScoreItem(CommandPaletteItem item, string query)
        {
            int best = HangulSearchHelper.Score(item.Title, query);
            int catScore = HangulSearchHelper.Score(item.Category, query);
            if (catScore > best) best = catScore;
            foreach (var alias in item.Aliases)
            {
                int s = HangulSearchHelper.Score(alias, query);
                if (s > best) best = s;
            }
            return best;
        }

        // ── 컨텍스트 기반 활성화 ────────────────────────────────

        private bool IsCommandAvailable(CommandPaletteItem item)
        {
            var commandId = item.CommandId;
            var viewMode = ViewModel.CurrentViewMode;
            int selectedCount = 0;
            try { selectedCount = GetCurrentSelectedItems()?.Count ?? 0; } catch { }

            bool isFileMode = viewMode == ViewMode.MillerColumns
                || viewMode == ViewMode.Details
                || viewMode == ViewMode.List
                || viewMode == ViewMode.IconSmall
                || viewMode == ViewMode.IconMedium
                || viewMode == ViewMode.IconLarge
                || viewMode == ViewMode.IconExtraLarge;

            // 탭 항목은 항상 사용 가능
            if (item.Type == CommandPaletteItemType.Tab) return true;

            // 파일 작업 명령: file 모드가 아니면 비활성
            switch (commandId)
            {
                case ShortcutCommands.Copy:
                case ShortcutCommands.Cut:
                case ShortcutCommands.Delete:
                case ShortcutCommands.PermanentDelete:
                case ShortcutCommands.Rename:
                case ShortcutCommands.Duplicate:
                case ShortcutCommands.ShowProperties:
                case ShortcutCommands.OpenInNewTab:
                case ShortcutCommands.PasteAsShortcut:
                    if (!isFileMode) return false;
                    if (selectedCount == 0) return false;
                    return true;

                case ShortcutCommands.NewFolder:
                case ShortcutCommands.Paste:
                    return isFileMode;

                case ShortcutCommands.NavigateBack:
                case ShortcutCommands.NavigateForward:
                case ShortcutCommands.NavigateUp:
                    if (viewMode == ViewMode.RecycleBin) return false;
                    return true;

                case ShortcutCommands.SelectAll:
                case ShortcutCommands.SelectNone:
                case ShortcutCommands.InvertSelection:
                case ShortcutCommands.Refresh:
                    return isFileMode;

                case ShortcutCommands.QuickLook:
                    if (!isFileMode || selectedCount == 0) return false;
                    return App.Current.Services.GetRequiredService<ISettingsService>().EnableQuickLook;

                case ShortcutCommands.SwitchPane:
                    return ViewModel.IsSplitViewEnabled;

                case ShortcutCommands.OpenSettings:
                    // 이미 Settings 탭이 열려 있으면 비활성
                    return !ViewModel.Tabs.Any(t => t.ViewMode == ViewMode.Settings);

                case ShortcutCommands.ShelfAdd:
                case ShortcutCommands.ShelfMoveHere:
                case ShortcutCommands.ShelfCopyHere:
                case ShortcutCommands.ShelfToggle:
                    return App.Current.Services.GetRequiredService<ISettingsService>().ShelfEnabled;

                case ShortcutCommands.ShelfClear:
                    return ViewModel.ShelfItems.Count > 0;

                case ShortcutCommands.OpenWorkspacePalette:
                case ShortcutCommands.SaveWorkspace:
                    return true;
            }

            return true;
        }

        // ── 카탈로그 빌드 ───────────────────────────────────────

        private List<CommandPaletteItem> BuildCommandCatalog()
        {
            var catalog = new List<CommandPaletteItem>();
            var keyBindingSvc = _keyBindingService ??= App.Current.Services.GetRequiredService<KeyBindingService>();
            var bindings = keyBindingSvc.CloneCurrentBindings();
            var settings = App.Current.Services.GetRequiredService<ISettingsService>();

            // 1. 등록된 모든 명령 (Settings 명령은 별도로 처리하므로 제외)
            foreach (var cmdId in ShortcutCommands.GetAllCommands())
            {
                if (cmdId.StartsWith("span.settings.")) continue;

                var displayName = ShortcutCommands.GetDisplayName(cmdId);
                var category = ShortcutCommands.GetCategory(cmdId);
                var shortcut = bindings.TryGetValue(cmdId, out var keys) && keys.Count > 0
                    ? keys[0] : string.Empty;

                catalog.Add(new CommandPaletteItem
                {
                    Title = displayName,
                    Category = LocalizeCategory(category),
                    GroupName = LocalizeCategory(category),
                    CommandId = cmdId,
                    Shortcut = shortcut,
                    Type = CommandPaletteItemType.Command,
                    IconGlyph = GetCommandIconGlyph(category),
                });
            }

            // 2. Settings: Toggle 명령
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleHidden, "Settings_ShowHiddenFiles", settings.ShowHiddenFiles, new[] { "hidden", "숨김" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleExtensions, "Settings_ShowFileExtensions", settings.ShowFileExtensions, new[] { "extension", "확장자" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleCheckboxes, "Settings_ShowCheckboxes", settings.ShowCheckboxes, new[] { "checkbox", "체크박스" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleThumbnails, "Settings_ShowThumbnails", settings.ShowThumbnails, new[] { "thumb", "썸네일" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleQuickLook, "Settings_EnableQuickLook", settings.EnableQuickLook, new[] { "quicklook", "preview", "미리보기" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleWasd, "Settings_EnableWasdNavigation", settings.EnableWasdNavigation, new[] { "wasd", "키보드" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleConfirmDelete, "Settings_ConfirmDelete", settings.ConfirmDelete, new[] { "delete", "삭제확인" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsTogglePreviewFolderInfo, "Settings_PreviewFolderInfo", settings.PreviewShowFolderInfo, new[] { "folder info", "폴더정보" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleDefaultPreview, "Settings_DefaultPreview", settings.DefaultPreviewEnabled, new[] { "default preview", "기본미리보기" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleFavoritesTree, "Settings_ShowFavoritesTree", settings.ShowFavoritesTree, new[] { "favorites tree", "즐겨찾기 트리" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleShelf, "Settings_ShelfEnabled", settings.ShelfEnabled, new[] { "shelf", "선반" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleShelfSave, "Settings_ShelfSave", settings.ShelfSaveEnabled, new[] { "shelf save", "선반 저장" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleContextMenu, "Settings_ShowContextMenu", settings.ShowContextMenu, new[] { "context menu", "컨텍스트" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleTray, "Settings_MinimizeToTray", settings.MinimizeToTray, new[] { "tray", "트레이" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleWindowPosition, "Settings_RememberWindowPosition", settings.RememberWindowPosition, new[] { "window position", "창위치" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleGitIntegration, "Settings_ShowGitIntegration", settings.ShowGitIntegration, new[] { "git", "깃" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleHexPreview, "Settings_ShowHexPreview", settings.ShowHexPreview, new[] { "hex", "헥스" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleFileHash, "Settings_ShowFileHash", settings.ShowFileHash, new[] { "hash", "해시" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleShellExtensions, "Settings_ShowShellExtensions", settings.ShowShellExtensions, new[] { "shell ext", "셸확장" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleWindowsShellExtras, "Settings_ShowWindowsShellExtras", settings.ShowWindowsShellExtras, new[] { "windows shell", "윈도우셸" });
            AddSettingToggle(catalog, ShortcutCommands.SettingsToggleCopilotMenu, "Settings_ShowCopilotMenu", settings.ShowCopilotMenu, new[] { "copilot", "코파일럿" });

            // 3. Sidebar 토글
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarHome, "Settings_SidebarShowHome", settings.SidebarShowHome, new[] { "sidebar home", "사이드바 홈" }, "Sidebar");
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarFavorites, "Settings_SidebarShowFavorites", settings.SidebarShowFavorites, new[] { "sidebar favorites", "사이드바 즐겨찾기" }, "Sidebar");
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarDrives, "Settings_SidebarShowDrives", settings.SidebarShowLocalDrives, new[] { "sidebar drives", "사이드바 드라이브" }, "Sidebar");
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarCloud, "Settings_SidebarShowCloud", settings.SidebarShowCloud, new[] { "sidebar cloud", "사이드바 클라우드" }, "Sidebar");
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarNetwork, "Settings_SidebarShowNetwork", settings.SidebarShowNetwork, new[] { "sidebar network", "사이드바 네트워크" }, "Sidebar");
            AddSettingToggle(catalog, ShortcutCommands.SettingsSidebarRecycleBin, "Settings_SidebarShowRecycleBin", settings.SidebarShowRecycleBin, new[] { "sidebar recycle", "사이드바 휴지통" }, "Sidebar");

            // 4. Theme select
            AddSettingSelect(catalog, ShortcutCommands.SettingsThemeSystem, "Cmd_ThemeSystem", "Theme", settings.Theme == "system", new[] { "theme system", "테마 시스템" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsThemeLight, "Cmd_ThemeLight", "Theme", settings.Theme == "light", new[] { "theme light", "라이트", "밝은" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsThemeDark, "Cmd_ThemeDark", "Theme", settings.Theme == "dark", new[] { "theme dark", "다크", "어두운" });

            // 5. Density select
            AddSettingSelect(catalog, ShortcutCommands.SettingsDensityCompact, "Cmd_DensityCompact", "Density", settings.Density == "compact", new[] { "density compact", "조밀" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsDensityComfortable, "Cmd_DensityComfortable", "Density", settings.Density == "comfortable", new[] { "density comfortable", "기본" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsDensitySpacious, "Cmd_DensitySpacious", "Density", settings.Density == "spacious", new[] { "density spacious", "넓은" });

            // 6. Language select
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageSystem, "Cmd_LangSystem", "Language", settings.Language == "system", new[] { "language system", "언어 시스템" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageEn, "Cmd_LangEn", "Language", settings.Language == "en", new[] { "english", "영어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageKo, "Cmd_LangKo", "Language", settings.Language == "ko", new[] { "korean", "한국어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageJa, "Cmd_LangJa", "Language", settings.Language == "ja", new[] { "japanese", "일본어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageZhHans, "Cmd_LangZhHans", "Language", settings.Language == "zh-Hans", new[] { "chinese simplified", "중국어 간체" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageZhHant, "Cmd_LangZhHant", "Language", settings.Language == "zh-Hant", new[] { "chinese traditional", "중국어 번체" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageDe, "Cmd_LangDe", "Language", settings.Language == "de", new[] { "german", "독일어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageEs, "Cmd_LangEs", "Language", settings.Language == "es", new[] { "spanish", "스페인어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguageFr, "Cmd_LangFr", "Language", settings.Language == "fr", new[] { "french", "프랑스어" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsLanguagePtBr, "Cmd_LangPtBr", "Language", settings.Language == "pt-BR", new[] { "portuguese", "포르투갈어" });

            // 7. Icon Pack
            AddSettingSelect(catalog, ShortcutCommands.SettingsIconPackRemix, "Cmd_IconRemix", "IconPack", settings.IconPack == "remix", new[] { "icon remix" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsIconPackPhosphor, "Cmd_IconPhosphor", "IconPack", settings.IconPack == "phosphor", new[] { "icon phosphor" });
            AddSettingSelect(catalog, ShortcutCommands.SettingsIconPackTabler, "Cmd_IconTabler", "IconPack", settings.IconPack == "tabler", new[] { "icon tabler" });

            // 8. Settings Sections
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenGeneral, "Cmd_OpenGeneral");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenAppearance, "Cmd_OpenAppearance");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenBrowsing, "Cmd_OpenBrowsing");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenSidebar, "Cmd_OpenSidebar");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenTools, "Cmd_OpenTools");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenShortcuts, "Cmd_OpenShortcuts");
            AddSettingsSection(catalog, ShortcutCommands.SettingsOpenAdvanced, "Cmd_OpenAdvanced");

            // 9. Open tabs
            for (int i = 0; i < ViewModel.Tabs.Count; i++)
            {
                var tab = ViewModel.Tabs[i];
                catalog.Add(new CommandPaletteItem
                {
                    Title = tab.Header ?? "Tab",
                    Category = _loc.Get("CommandPalette_Tabs"),
                    GroupName = _loc.Get("CommandPalette_Tabs"),
                    TabIndex = i,
                    Type = CommandPaletteItemType.Tab,
                    IconGlyph = "\uE737",
                    Aliases = { "tab", "탭" },
                });
            }

            return catalog;
        }

        private void AddSettingToggle(List<CommandPaletteItem> catalog, string commandId, string locKey, bool currentValue, string[] aliases, string? group = null)
        {
            var label = _loc.Get(locKey);
            var stateText = currentValue ? _loc.Get("Cmd_StateOn") : _loc.Get("Cmd_StateOff");
            var groupName = group ?? _loc.Get("Cmd_Group_Settings");
            var item = new CommandPaletteItem
            {
                Title = $"{label} ({stateText})",
                Category = groupName,
                GroupName = groupName,
                CommandId = commandId,
                Type = CommandPaletteItemType.SettingToggle,
                IconGlyph = currentValue ? "\uE73E" : "\uE711", // 체크 / X
                CurrentStateText = stateText,
            };
            foreach (var a in aliases) item.Aliases.Add(a);
            catalog.Add(item);
        }

        private void AddSettingSelect(List<CommandPaletteItem> catalog, string commandId, string titleLocKey, string groupKey, bool isCurrent, string[] aliases)
        {
            var groupName = _loc.Get($"Cmd_Group_{groupKey}");
            var titleBase = _loc.Get(titleLocKey);
            var item = new CommandPaletteItem
            {
                Title = isCurrent ? $"{titleBase}  ●" : titleBase,
                Category = groupName,
                GroupName = groupName,
                CommandId = commandId,
                Type = CommandPaletteItemType.SettingSelect,
                IconGlyph = isCurrent ? "\uE915" : "\uE9CE", // selected / circle
            };
            foreach (var a in aliases) item.Aliases.Add(a);
            catalog.Add(item);
        }

        private void AddSettingsSection(List<CommandPaletteItem> catalog, string commandId, string titleLocKey)
        {
            var groupName = _loc.Get("Cmd_Group_GoToSettings");
            catalog.Add(new CommandPaletteItem
            {
                Title = _loc.Get(titleLocKey),
                Category = groupName,
                GroupName = groupName,
                CommandId = commandId,
                Type = CommandPaletteItemType.SettingsSection,
                IconGlyph = "\uE713", // gear
                Aliases = { "settings", "설정" },
            });
        }

        private string LocalizeCategory(string category) => category switch
        {
            "Navigation" => _loc.Get("Cmd_Cat_Navigation"),
            "Edit" => _loc.Get("Cmd_Cat_Edit"),
            "Selection" => _loc.Get("Cmd_Cat_Selection"),
            "View" => _loc.Get("Cmd_Cat_View"),
            "Tab" => _loc.Get("Cmd_Cat_Tab"),
            "Window" => _loc.Get("Cmd_Cat_Window"),
            "Workspace" => _loc.Get("Cmd_Cat_Workspace"),
            "Shelf" => _loc.Get("Cmd_Cat_Shelf"),
            "QuickLook" => _loc.Get("Cmd_Cat_QuickLook"),
            "CommandPalette" => _loc.Get("Cmd_Cat_CommandPalette"),
            _ => category,
        };

        private static string GetCommandIconGlyph(string category) => category switch
        {
            "Navigation" => "\uE72A",
            "Edit" => "\uE70F",
            "Selection" => "\uE762",
            "View" => "\uE8A9",
            "Tab" => "\uE737",
            "Window" => "\uE8A7",
            "Shelf" => "\uE8F1",
            "CommandPalette" => "\uE773",
            "Workspace" => "\uE74C",
            "QuickLook" => "\uE8FF",
            _ => "\uE756",
        };

        // ── 최근 사용 추적 ──────────────────────────────────────

        private void TrackRecentCommand(string commandId)
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<ISettingsService>();
                var current = LoadRecentCommandIds();
                current.Remove(commandId);
                current.Insert(0, commandId);
                if (current.Count > MaxRecentCommands)
                    current.RemoveRange(MaxRecentCommands, current.Count - MaxRecentCommands);
                settings.RecentCommandIds = string.Join("|", current);
            }
            catch { }
        }

        private List<string> LoadRecentCommandIds()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<ISettingsService>();
                var raw = settings.RecentCommandIds;
                if (string.IsNullOrEmpty(raw)) return new List<string>();
                return raw.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            catch { return new List<string>(); }
        }
    }
}
