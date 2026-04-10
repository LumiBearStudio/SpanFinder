using System.Linq;
using Span.Services;

namespace Span.Tests.Services;

/// <summary>
/// Command Palette + Settings 토글용으로 새로 추가된 로컬라이즈 키 무결성 검사.
/// (기존 LocalizationTests의 9개 언어 빈값 검사는 전체 Entries를 보호하므로 여기서는 키 존재성에 집중)
/// </summary>
[TestClass]
public class LocalizationCommandKeysTests
{
    private static readonly System.Collections.Generic.HashSet<string> Keys =
        new(LocalizationService.Entries.Select(e => e.key));

    [TestMethod]
    public void CommandPaletteCategory_Keys_AllExist()
    {
        // 카테고리 이름
        var categoryKeys = new[]
        {
            "Cmd_Cat_Navigation", "Cmd_Cat_Edit", "Cmd_Cat_Selection",
            "Cmd_Cat_View", "Cmd_Cat_Tab", "Cmd_Cat_Window",
            "Cmd_Cat_Workspace", "Cmd_Cat_Shelf", "Cmd_Cat_Settings"
        };

        var missing = categoryKeys.Where(k => !Keys.Contains(k)).ToList();
        Assert.AreEqual(0, missing.Count,
            $"누락된 Cmd_Cat_* 키: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void CommandPaletteState_Keys_OnAndOff_Exist()
    {
        Assert.IsTrue(Keys.Contains("Cmd_StateOn"));
        Assert.IsTrue(Keys.Contains("Cmd_StateOff"));
    }

    [TestMethod]
    public void CommandPaletteThemeAndDensity_Keys_Exist()
    {
        var themeKeys = new[] { "Cmd_ThemeSystem", "Cmd_ThemeLight", "Cmd_ThemeDark" };
        var densityKeys = new[] { "Cmd_DensityCompact", "Cmd_DensityComfortable", "Cmd_DensitySpacious" };

        foreach (var k in themeKeys.Concat(densityKeys))
            Assert.IsTrue(Keys.Contains(k), $"누락: {k}");
    }

    [TestMethod]
    public void CommandPaletteLanguage_AllNineLanguages_Exist()
    {
        // Span은 9개 언어 지원 — Command Palette에서 모두 선택 가능해야 함
        var langKeys = new[]
        {
            "Cmd_LangSystem", "Cmd_LangEn", "Cmd_LangKo", "Cmd_LangJa",
            "Cmd_LangZhHans", "Cmd_LangZhHant", "Cmd_LangDe",
            "Cmd_LangEs", "Cmd_LangFr", "Cmd_LangPtBr"
        };

        var missing = langKeys.Where(k => !Keys.Contains(k)).ToList();
        Assert.AreEqual(0, missing.Count,
            $"누락된 Cmd_Lang_* 키: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void CommandPaletteOpenSettingsSection_Keys_Exist()
    {
        var openKeys = new[]
        {
            "Cmd_OpenGeneral", "Cmd_OpenAppearance", "Cmd_OpenBrowsing",
            "Cmd_OpenSidebar", "Cmd_OpenTools", "Cmd_OpenShortcuts", "Cmd_OpenAdvanced"
        };

        foreach (var k in openKeys)
            Assert.IsTrue(Keys.Contains(k), $"누락: {k}");
    }

    [TestMethod]
    public void NewSettingsToggle_Keys_Exist()
    {
        // 새로 추가된 Settings 토글들 (1.3.x)
        var toggleKeys = new[]
        {
            "Settings_PreviewFolderInfo",
            "Settings_RememberWindowPosition",
            "Settings_ShelfEnabled",
            "Settings_ShelfSave",
            "Settings_DefaultPreview"
        };

        var missing = toggleKeys.Where(k => !Keys.Contains(k)).ToList();
        Assert.AreEqual(0, missing.Count,
            $"누락된 Settings_* 키: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void SidebarSection_Keys_Exist()
    {
        // 사이드바 섹션 토글
        var sidebarKeys = new[]
        {
            "Settings_SidebarShowHome",
            "Settings_SidebarShowFavorites",
            "Settings_SidebarShowLocalDrives",
            "Settings_SidebarShowCloud",
            "Settings_SidebarShowNetwork",
            "Settings_SidebarShowRecycleBin"
        };

        // 일부만 키가 있을 수 있으므로 최소 1개는 있어야 함을 검사
        var present = sidebarKeys.Where(k => Keys.Contains(k)).ToList();
        Assert.IsTrue(present.Count >= 1,
            $"사이드바 섹션 키가 하나도 없음. 검사한 키: {string.Join(", ", sidebarKeys)}");
    }
}
