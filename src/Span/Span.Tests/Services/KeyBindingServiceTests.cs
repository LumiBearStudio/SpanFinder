using System.Collections.Generic;
using Span.Models;
using Span.Services;
using Windows.System;

namespace Span.Tests.Services;

[TestClass]
public class KeyBindingServiceTests
{
    private SettingsServiceStub _settings = null!;
    private KeyBindingService _service = null!;

    [TestInitialize]
    public void Init()
    {
        _settings = new SettingsServiceStub();
        _service = new KeyBindingService(_settings);
    }

    // ── Default bindings ────────────────────────────

    [TestMethod]
    public void Defaults_ContainCoreNavigationCommands()
    {
        var defaults = _service.GetDefaultBindings();

        CollectionAssert.AreEqual(new[] { "Alt+Left" }, defaults[ShortcutCommands.NavigateBack]);
        CollectionAssert.AreEqual(new[] { "Alt+Right" }, defaults[ShortcutCommands.NavigateForward]);
        CollectionAssert.AreEqual(new[] { "Alt+Up" }, defaults[ShortcutCommands.NavigateUp]);
    }

    [TestMethod]
    public void Defaults_TerminalHasBothBacktickAndQuoteForKoreanFallback()
    {
        var defaults = _service.GetDefaultBindings();
        CollectionAssert.Contains(defaults[ShortcutCommands.OpenTerminal], "Ctrl+`");
        CollectionAssert.Contains(defaults[ShortcutCommands.OpenTerminal], "Ctrl+'");
    }

    [TestMethod]
    public void Defaults_DoNotIncludeHiddenCommandPalette()
    {
        // 2026-04-10: Command Palette는 숨김 처리되어 기본 단축키 매핑이 없어야 함
        var defaults = _service.GetDefaultBindings();
        Assert.IsFalse(defaults.ContainsKey(ShortcutCommands.OpenCommandPalette),
            "OpenCommandPalette는 기본 매핑에서 제외되어야 한다 (숨김 처리)");
    }

    [TestMethod]
    public void Defaults_AreCloned_NotShared()
    {
        var d1 = _service.GetDefaultBindings();
        var d2 = _service.GetDefaultBindings();
        d1[ShortcutCommands.NavigateBack].Add("Modified");

        Assert.IsFalse(d2[ShortcutCommands.NavigateBack].Contains("Modified"),
            "GetDefaultBindings는 매번 deep copy를 반환해야 한다");
    }

    // ── ResolveCommand ──────────────────────────────

    [TestMethod]
    public void Resolve_AltLeft_NavigatesBack()
    {
        var cmd = _service.ResolveCommand(VirtualKey.Left, ctrl: false, shift: false, alt: true, scanCode: 0);
        Assert.AreEqual(ShortcutCommands.NavigateBack, cmd);
    }

    [TestMethod]
    public void Resolve_F5_Refresh()
    {
        var cmd = _service.ResolveCommand(VirtualKey.F5, false, false, false, 0);
        Assert.AreEqual(ShortcutCommands.Refresh, cmd);
    }

    [TestMethod]
    public void Resolve_CtrlC_Copy()
    {
        var cmd = _service.ResolveCommand(VirtualKey.C, ctrl: true, shift: false, alt: false, scanCode: 0);
        Assert.AreEqual(ShortcutCommands.Copy, cmd);
    }

    [TestMethod]
    public void Resolve_Unbound_ReturnsNull()
    {
        var cmd = _service.ResolveCommand(VirtualKey.Q, ctrl: true, shift: true, alt: true, scanCode: 0);
        Assert.IsNull(cmd);
    }

    [TestMethod]
    public void Resolve_KoreanBacktickFallback_UsesScanCode()
    {
        // 한국어 키보드: Ctrl+` 의 VirtualKey가 일반적이지 않을 때 ScanCode=41이 fallback으로 동작
        // 임의의 VirtualKey와 함께 ScanCode 41을 보내면 Ctrl+`로 인식되어야 함
        var cmd = _service.ResolveCommand((VirtualKey)0xFF, ctrl: true, shift: false, alt: false, scanCode: 41);
        Assert.AreEqual(ShortcutCommands.OpenTerminal, cmd);
    }

    [TestMethod]
    public void Resolve_CtrlComma_OpensSettings()
    {
        // Ctrl+, → OpenSettings (한국어 키보드 ScanCode=51)
        var cmd = _service.ResolveCommand((VirtualKey)0xFF, ctrl: true, shift: false, alt: false, scanCode: 51);
        Assert.AreEqual(ShortcutCommands.OpenSettings, cmd);
    }

    // ── BuildKeyString ──────────────────────────────

    [TestMethod]
    public void BuildKeyString_NoModifiers_JustKey()
    {
        Assert.AreEqual("F5", KeyBindingService.BuildKeyString(false, false, false, VirtualKey.F5));
    }

    [TestMethod]
    public void BuildKeyString_AllModifiers_OrdersCtrlShiftAlt()
    {
        Assert.AreEqual("Ctrl+Shift+Alt+A",
            KeyBindingService.BuildKeyString(true, true, true, VirtualKey.A));
    }

    [TestMethod]
    public void BuildKeyString_OemKeys_ConvertsToFriendlyName()
    {
        // OEM 188 = ','
        Assert.AreEqual("Ctrl+,",
            KeyBindingService.BuildKeyString(true, false, false, (VirtualKey)188));
        // OEM 192 = '`'
        Assert.AreEqual("Ctrl+`",
            KeyBindingService.BuildKeyString(true, false, false, (VirtualKey)192));
    }

    // ── IsSystemReserved ────────────────────────────

    [TestMethod]
    [DataRow("Alt+F4")]
    [DataRow("Alt+Tab")]
    [DataRow("Ctrl+Alt+Delete")]
    [DataRow("Ctrl+Shift+Escape")]
    public void IsSystemReserved_KnownReserved_True(string keyString)
    {
        Assert.IsTrue(_service.IsSystemReserved(keyString));
    }

    [TestMethod]
    public void IsSystemReserved_AnyWinModifier_True()
    {
        Assert.IsTrue(_service.IsSystemReserved("Win+E"));
        Assert.IsTrue(_service.IsSystemReserved("Win+Shift+S"));
    }

    [TestMethod]
    public void IsSystemReserved_NormalKey_False()
    {
        Assert.IsFalse(_service.IsSystemReserved("Ctrl+K"));
        Assert.IsFalse(_service.IsSystemReserved("F5"));
        Assert.IsFalse(_service.IsSystemReserved(""));
    }

    // ── IsStructuralKey ─────────────────────────────

    [TestMethod]
    [DataRow("Left")]
    [DataRow("Right")]
    [DataRow("Up")]
    [DataRow("Down")]
    [DataRow("Enter")]
    [DataRow("Tab")]
    [DataRow("Escape")]
    public void IsStructuralKey_BareNavigation_True(string keyString)
    {
        Assert.IsTrue(_service.IsStructuralKey(keyString));
    }

    [TestMethod]
    public void IsStructuralKey_WithModifier_False()
    {
        Assert.IsFalse(_service.IsStructuralKey("Ctrl+Left"));
        Assert.IsFalse(_service.IsStructuralKey("Alt+Enter"));
    }

    // ── CheckConflict ───────────────────────────────

    [TestMethod]
    public void CheckConflict_DuplicateKey_ReturnsAlreadyAssigned()
    {
        var editing = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            [ShortcutCommands.Copy] = ["Ctrl+C"],
            [ShortcutCommands.Cut]  = ["Ctrl+X"],
        };

        var result = _service.CheckConflict("Ctrl+C", ShortcutCommands.Cut, editing);

        Assert.AreEqual(ConflictType.AlreadyAssigned, result.Type);
        Assert.AreEqual(ShortcutCommands.Copy, result.ExistingCommandId);
    }

    [TestMethod]
    public void CheckConflict_AssigningToSameCommand_NoConflict()
    {
        var editing = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            [ShortcutCommands.Copy] = ["Ctrl+C"],
        };

        var result = _service.CheckConflict("Ctrl+C", ShortcutCommands.Copy, editing);

        Assert.AreEqual(ConflictType.None, result.Type);
    }

    [TestMethod]
    public void CheckConflict_SystemReserved_BlocksAssignment()
    {
        var editing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var result = _service.CheckConflict("Alt+F4", ShortcutCommands.Copy, editing);

        Assert.AreEqual(ConflictType.SystemReserved, result.Type);
    }

    [TestMethod]
    public void CheckConflict_StructuralKey_BlocksAssignment()
    {
        var editing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var result = _service.CheckConflict("Enter", ShortcutCommands.Copy, editing);

        Assert.AreEqual(ConflictType.Structural, result.Type);
    }

    [TestMethod]
    public void CheckConflict_EmptyKey_NoConflict()
    {
        var editing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var result = _service.CheckConflict("", ShortcutCommands.Copy, editing);
        Assert.AreEqual(ConflictType.None, result.Type);
    }

    // ── ApplyAndSave / Persistence ──────────────────

    [TestMethod]
    public void ApplyAndSave_OverridesPersist_AcrossInstances()
    {
        var bindings = _service.CloneCurrentBindings();
        bindings[ShortcutCommands.Copy] = ["Ctrl+Shift+C"];
        _service.ApplyAndSave(bindings);

        // 새 인스턴스를 만들면 같은 settings stub에서 오버라이드를 로드해야 함
        var fresh = new KeyBindingService(_settings);
        var loaded = fresh.CloneCurrentBindings();

        CollectionAssert.AreEqual(new[] { "Ctrl+Shift+C" }, loaded[ShortcutCommands.Copy]);
    }

    [TestMethod]
    public void ApplyAndSave_ResolveReflectsNewBinding()
    {
        var bindings = _service.CloneCurrentBindings();
        bindings[ShortcutCommands.Copy] = ["Ctrl+Shift+Y"];
        _service.ApplyAndSave(bindings);

        var cmd = _service.ResolveCommand(VirtualKey.Y, ctrl: true, shift: true, alt: false, scanCode: 0);
        Assert.AreEqual(ShortcutCommands.Copy, cmd);
    }

    [TestMethod]
    public void ApplyAndSave_DefaultsUnchanged_NoJsonStored()
    {
        // 기본값 그대로 저장하면 JSON이 비어있어야 함 (오버라이드 없음)
        var bindings = _service.CloneCurrentBindings();
        _service.ApplyAndSave(bindings);

        var json = _settings.Get<string>("KeyBindingsJson", "MISSING");
        Assert.AreEqual(string.Empty, json,
            "오버라이드가 없으면 JSON 키는 빈 문자열로 클리어되어야 한다");
    }

    [TestMethod]
    public void Load_CorruptedJson_FallsBackToDefaults()
    {
        _settings.Set("KeyBindingsJson", "{garbage not json");

        var fresh = new KeyBindingService(_settings);
        var loaded = fresh.CloneCurrentBindings();

        CollectionAssert.AreEqual(new[] { "Ctrl+C" }, loaded[ShortcutCommands.Copy]);
        // 손상된 JSON은 클리어되어야 함
        Assert.AreEqual(string.Empty, _settings.Get<string>("KeyBindingsJson", "x"));
    }

    [TestMethod]
    public void Load_UnknownCommandInOverride_Ignored()
    {
        // 알 수 없는 commandId가 들어 있는 오버라이드는 무시되어야 함
        var json = """{"version":1,"overrides":[{"command":"span.unknown.command","keys":["Ctrl+Q"]}]}""";
        _settings.Set("KeyBindingsJson", json);

        var fresh = new KeyBindingService(_settings);
        // 기존 기본값들은 유지
        var loaded = fresh.CloneCurrentBindings();
        Assert.IsTrue(loaded.ContainsKey(ShortcutCommands.Copy));
    }

    // ── ConflictResult ──────────────────────────────

    [TestMethod]
    public void NoConflict_Singleton_HasNoneType()
    {
        Assert.AreEqual(ConflictType.None, ConflictResult.NoConflict.Type);
    }
}
