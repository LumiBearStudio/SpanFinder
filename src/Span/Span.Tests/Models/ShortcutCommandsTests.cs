using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class ShortcutCommandsTests
{
    /// <summary>
    /// 모든 public const string 상수(commandId)를 reflection으로 수집한다.
    /// 새 단축키 추가 시 이 헬퍼 한 곳에서 자동으로 인식된다.
    /// </summary>
    private static List<(string Name, string Value)> GetAllConstants()
    {
        return typeof(ShortcutCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetValue(null)!))
            .ToList();
    }

    // ── 상수 자체 ───────────────────────────────────

    [TestMethod]
    public void AllCommandIds_StartWithSpanPrefix()
    {
        foreach (var (name, value) in GetAllConstants())
        {
            Assert.IsTrue(value.StartsWith("span."),
                $"{name} = '{value}' 는 'span.' 으로 시작해야 한다");
        }
    }

    [TestMethod]
    public void AllCommandIds_AreUnique()
    {
        var grouped = GetAllConstants()
            .GroupBy(c => c.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.AreEqual(0, grouped.Count,
            $"중복된 commandId: {string.Join(", ", grouped.Select(g => g.Key))}");
    }

    [TestMethod]
    public void AllCommandIds_HaveAtLeastThreeSegments()
    {
        // "span.category.action" 형식
        foreach (var (name, value) in GetAllConstants())
        {
            var parts = value.Split('.');
            Assert.IsTrue(parts.Length >= 3,
                $"{name} = '{value}' 는 'span.카테고리.액션' 형식이어야 한다");
        }
    }

    // ── _categories 레지스트리와의 일관성 ───────────

    [TestMethod]
    public void EveryConstant_IsRegisteredInCategoriesMap()
    {
        var registered = new HashSet<string>(ShortcutCommands.GetAllCommands());
        var missing = new List<string>();

        foreach (var (name, value) in GetAllConstants())
        {
            if (!registered.Contains(value))
                missing.Add($"{name}({value})");
        }

        Assert.AreEqual(0, missing.Count,
            $"_categories에 미등록된 commandId: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void GetAllCommands_HasNoDuplicates()
    {
        var all = ShortcutCommands.GetAllCommands();
        Assert.AreEqual(all.Count, all.Distinct().Count(),
            "GetAllCommands는 중복 없이 반환해야 한다");
    }

    [TestMethod]
    public void GetAllCommands_ReturnsAtLeast70()
    {
        // 90+ 명령이 등록되어 있어야 한다 (Settings 토글 + 사이드바 + 테마 등 포함)
        var all = ShortcutCommands.GetAllCommands();
        Assert.IsTrue(all.Count >= 70,
            $"등록된 commandId가 너무 적다: {all.Count}개");
    }

    // ── GetCategory ─────────────────────────────────

    [TestMethod]
    public void GetCategory_KnownCommand_ReturnsCategory()
    {
        Assert.AreEqual("Edit", ShortcutCommands.GetCategory(ShortcutCommands.Copy));
        Assert.AreEqual("Navigation", ShortcutCommands.GetCategory(ShortcutCommands.NavigateBack));
        Assert.AreEqual("View", ShortcutCommands.GetCategory(ShortcutCommands.Refresh));
    }

    [TestMethod]
    public void GetCategory_UnknownCommand_ReturnsUnknown()
    {
        Assert.AreEqual("Unknown", ShortcutCommands.GetCategory("span.bogus.cmd"));
    }

    [TestMethod]
    public void EveryCommand_BelongsToValidCategory()
    {
        var validCategories = new HashSet<string>(ShortcutCommands.GetAllCategories());
        foreach (var cmd in ShortcutCommands.GetAllCommands())
        {
            var cat = ShortcutCommands.GetCategory(cmd);
            Assert.AreNotEqual("Unknown", cat, $"{cmd}의 카테고리가 Unknown");
            Assert.IsTrue(validCategories.Contains(cat),
                $"{cmd}의 카테고리 '{cat}'가 GetAllCategories 결과에 없다");
        }
    }

    // ── GetAllCategories ────────────────────────────

    [TestMethod]
    public void GetAllCategories_NoDuplicates()
    {
        var cats = ShortcutCommands.GetAllCategories();
        Assert.AreEqual(cats.Count, cats.Distinct().Count(),
            "GetAllCategories는 distinct여야 한다");
    }

    [TestMethod]
    public void GetAllCategories_ContainsCoreCategories()
    {
        var cats = new HashSet<string>(ShortcutCommands.GetAllCategories());
        CollectionAssert.IsSubsetOf(
            new[] { "Navigation", "Edit", "Selection", "View", "Tab", "Window", "Shelf", "Settings" },
            cats.ToList());
    }

    // ── GetCommandsByCategory ───────────────────────

    [TestMethod]
    public void GetCommandsByCategory_Edit_ContainsCopyAndPaste()
    {
        var editCommands = ShortcutCommands.GetCommandsByCategory("Edit");
        CollectionAssert.Contains(editCommands.ToList(), ShortcutCommands.Copy);
        CollectionAssert.Contains(editCommands.ToList(), ShortcutCommands.Paste);
        CollectionAssert.Contains(editCommands.ToList(), ShortcutCommands.Delete);
    }

    [TestMethod]
    public void GetCommandsByCategory_Unknown_ReturnsEmpty()
    {
        var result = ShortcutCommands.GetCommandsByCategory("NoSuchCategory");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Categories_PartitionAllCommands()
    {
        // 모든 카테고리의 명령 합집합 = 전체 명령
        var all = new HashSet<string>(ShortcutCommands.GetAllCommands());
        var collected = new HashSet<string>();
        foreach (var cat in ShortcutCommands.GetAllCategories())
        {
            foreach (var cmd in ShortcutCommands.GetCommandsByCategory(cat))
                collected.Add(cmd);
        }

        Assert.AreEqual(all.Count, collected.Count, "카테고리 분할이 전체 명령과 일치해야 한다");
    }

    // ── GetDisplayName ──────────────────────────────

    [TestMethod]
    public void GetDisplayName_KnownCommand_ReturnsNonEmpty()
    {
        var name = ShortcutCommands.GetDisplayName(ShortcutCommands.Copy);
        Assert.IsFalse(string.IsNullOrEmpty(name));
    }

    [TestMethod]
    public void GetDisplayName_UnknownCommand_ReturnsLastSegment()
    {
        // fallback: 마지막 점 뒤 세그먼트
        var name = ShortcutCommands.GetDisplayName("span.foo.bar");
        Assert.AreEqual("bar", name);
    }

    [TestMethod]
    public void GetDisplayName_NoDot_ReturnsAsIs()
    {
        var name = ShortcutCommands.GetDisplayName("noDot");
        Assert.AreEqual("noDot", name);
    }

    // ── IsRemappable ────────────────────────────────

    [TestMethod]
    public void IsRemappable_NormalCommand_True()
    {
        Assert.IsTrue(ShortcutCommands.IsRemappable(ShortcutCommands.Copy));
        Assert.IsTrue(ShortcutCommands.IsRemappable(ShortcutCommands.NavigateBack));
    }

    [TestMethod]
    public void IsRemappable_F11Fullscreen_False()
    {
        // Fullscreen은 OS 수준 — 리매핑 불가
        Assert.IsFalse(ShortcutCommands.IsRemappable(ShortcutCommands.Fullscreen));
    }

    [TestMethod]
    public void IsRemappable_UnknownCommand_False()
    {
        Assert.IsFalse(ShortcutCommands.IsRemappable("span.no.such"));
    }

    // ── 숨김 처리된 Command Palette 보호 ────────────

    [TestMethod]
    public void OpenCommandPalette_StillRegisteredEvenThoughHidden()
    {
        // 2026-04-10: Command Palette UI는 숨김 처리되었지만, 상수와 카테고리는 유지되어야 함
        // (사용자가 Settings에서 키 재할당 시 즉시 동작)
        Assert.IsTrue(ShortcutCommands.GetAllCommands().Contains(ShortcutCommands.OpenCommandPalette));
        Assert.AreEqual("CommandPalette",
            ShortcutCommands.GetCategory(ShortcutCommands.OpenCommandPalette));
    }
}
