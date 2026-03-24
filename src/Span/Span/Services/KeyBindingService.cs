using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.System;
using Span.Helpers;

namespace Span.Services;

/// <summary>
/// 키보드 단축키 바인딩 관리 서비스.
/// 기본 바인딩 테이블(불변) + 사용자 오버라이드(JSON) → 런타임 병합.
/// ResolveCommand는 OnGlobalKeyDown에서 호출되므로 성능 최적화(역인덱스) 적용.
/// </summary>
public class KeyBindingService
{
    private const string SettingsKey = "KeyBindingsJson";
    private const int CurrentVersion = 1;

    private readonly ISettingsService _settingsService;

    // 기본 바인딩: commandId → keys (불변, 코드 내장)
    private readonly Dictionary<string, List<string>> _defaultBindings;

    // 현재 활성 바인딩: commandId → keys
    private Dictionary<string, List<string>> _currentBindings;

    // 역인덱스: keyString → commandId (ResolveCommand 성능용)
    private Dictionary<string, string> _reverseIndex;

    // ScanCode → VirtualKey 이름 매핑 (한국어/일본어 IME fallback)
    private static readonly Dictionary<uint, string> ScanCodeToKeyName = new()
    {
        { 41, "`" },     // backtick
        { 40, "'" },    // single quote
        { 51, "," },    // comma
    };

    // 구조적 키: 수식키 없이 단독 사용 시 바인딩 차단
    private static readonly HashSet<string> StructuralKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Left", "Right", "Up", "Down",
        "Enter", "Back", "Tab",
        "Home", "End", "Escape"
    };

    // 시스템 예약 키: 바인딩 차단
    private static readonly HashSet<string> SystemReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+F4", "Alt+Tab", "Alt+Space",
        "Ctrl+Alt+Delete", "Ctrl+Shift+Escape",
        "Ctrl+Escape", "PrintScreen"
    };

    public KeyBindingService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _defaultBindings = BuildDefaultBindings();
        _currentBindings = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _reverseIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LoadAndMerge();
    }

    // ── Public API ──────────────────────────────────────────────

    /// <summary>
    /// 키 입력을 commandId로 변환. OnGlobalKeyDown에서 호출.
    /// VirtualKey 기반 매칭 실패 시 ScanCode fallback 시도.
    /// </summary>
    public string? ResolveCommand(VirtualKey key, bool ctrl, bool shift, bool alt, uint scanCode)
    {
        // 1차: VirtualKey 기반
        var keyString = BuildKeyString(ctrl, shift, alt, key);
        if (_reverseIndex.TryGetValue(keyString, out var commandId))
            return commandId;

        // 2차: ScanCode fallback (한국어/일본어 IME에서 VirtualKey가 달라지는 경우)
        if (scanCode > 0 && ScanCodeToKeyName.TryGetValue(scanCode, out var fallbackKeyName))
        {
            var fallbackKeyString = BuildKeyStringRaw(ctrl, shift, alt, fallbackKeyName);
            if (_reverseIndex.TryGetValue(fallbackKeyString, out commandId))
                return commandId;
        }

        return null;
    }

    /// <summary>
    /// 편집 UI용 현재 바인딩 사본 반환.
    /// </summary>
    public Dictionary<string, List<string>> CloneCurrentBindings()
    {
        return _currentBindings.ToDictionary(
            kvp => kvp.Key,
            kvp => new List<string>(kvp.Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 기본 바인딩 사본 반환 (리셋용).
    /// </summary>
    public Dictionary<string, List<string>> GetDefaultBindings()
    {
        return _defaultBindings.ToDictionary(
            kvp => kvp.Key,
            kvp => new List<string>(kvp.Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 편집 결과를 적용하고 오버라이드를 JSON으로 저장.
    /// </summary>
    public void ApplyAndSave(Dictionary<string, List<string>> bindings)
    {
        _currentBindings = bindings.ToDictionary(
            kvp => kvp.Key,
            kvp => new List<string>(kvp.Value),
            StringComparer.Ordinal);

        RebuildReverseIndex();
        SaveOverrides();
    }

    /// <summary>
    /// 편집 사본 기준 충돌 검사.
    /// </summary>
    public ConflictResult CheckConflict(
        string keyString,
        string targetCommandId,
        Dictionary<string, List<string>> editingBindings)
    {
        if (string.IsNullOrWhiteSpace(keyString))
            return ConflictResult.NoConflict;

        // 시스템 예약 키 검사
        if (IsSystemReserved(keyString))
        {
            return new ConflictResult
            {
                Type = ConflictType.SystemReserved,
                Message = $"'{keyString}' is reserved by the system and cannot be assigned."
            };
        }

        // 구조적 키 검사 (수식키 없는 단독 키)
        if (IsStructuralKey(keyString))
        {
            return new ConflictResult
            {
                Type = ConflictType.Structural,
                Message = $"'{keyString}' is a structural navigation key and cannot be reassigned."
            };
        }

        // 다른 커맨드에 이미 할당되어 있는지 검사
        foreach (var (commandId, keys) in editingBindings)
        {
            if (string.Equals(commandId, targetCommandId, StringComparison.Ordinal))
                continue;

            if (keys.Any(k => string.Equals(k, keyString, StringComparison.OrdinalIgnoreCase)))
            {
                return new ConflictResult
                {
                    Type = ConflictType.AlreadyAssigned,
                    ExistingCommandId = commandId,
                    ExistingCommandName = commandId, // UI에서 표시 이름으로 변환
                    Message = $"'{keyString}' is already assigned to '{commandId}'."
                };
            }
        }

        return ConflictResult.NoConflict;
    }

    /// <summary>
    /// 시스템 예약 키 여부. Win 수식자 포함 조합도 차단.
    /// </summary>
    public bool IsSystemReserved(string keyString)
    {
        if (string.IsNullOrWhiteSpace(keyString))
            return false;

        // Win 키 수식자 포함 조합 전부 차단
        if (keyString.StartsWith("Win+", StringComparison.OrdinalIgnoreCase))
            return true;

        return SystemReservedKeys.Contains(keyString);
    }

    /// <summary>
    /// 구조적 키 여부 (수식키 없는 단독 키).
    /// </summary>
    public bool IsStructuralKey(string keyString)
    {
        if (string.IsNullOrWhiteSpace(keyString))
            return false;

        // 수식키가 포함되어 있으면 구조적 키가 아님
        if (keyString.Contains('+'))
            return false;

        return StructuralKeys.Contains(keyString);
    }

    /// <summary>
    /// VirtualKey + 수식키 → 키 문자열 생성.
    /// </summary>
    public static string BuildKeyString(bool ctrl, bool shift, bool alt, VirtualKey key)
    {
        var keyName = VirtualKeyToString(key);
        return BuildKeyStringRaw(ctrl, shift, alt, keyName);
    }

    // ── Private: 바인딩 테이블 ────────────────────────────────

    private static Dictionary<string, List<string>> BuildDefaultBindings()
    {
        // commandId → keys[]
        return new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            // Navigation
            ["span.navigate.back"]       = ["Alt+Left"],
            ["span.navigate.forward"]    = ["Alt+Right"],
            ["span.navigate.up"]         = ["Alt+Up"],
            ["span.navigate.addressBar"] = ["Ctrl+L", "Alt+D", "F4"],
            ["span.navigate.search"]     = ["Ctrl+F", "F3"],
            ["span.navigate.filterBar"]  = ["Ctrl+Shift+F"],

            // Edit
            ["span.edit.copy"]            = ["Ctrl+C"],
            ["span.edit.cut"]             = ["Ctrl+X"],
            ["span.edit.paste"]           = ["Ctrl+V"],
            ["span.edit.pasteAsShortcut"] = ["Ctrl+Shift+V"],
            ["span.edit.delete"]          = ["Delete"],
            ["span.edit.permanentDelete"] = ["Shift+Delete"],
            ["span.edit.rename"]          = ["F2"],
            ["span.edit.duplicate"]       = ["Ctrl+D"],
            ["span.edit.newFolder"]       = ["Ctrl+Shift+N"],
            ["span.edit.undo"]            = ["Ctrl+Z"],
            ["span.edit.redo"]            = ["Ctrl+Y"],

            // Selection
            ["span.selection.selectAll"]  = ["Ctrl+A"],
            ["span.selection.selectNone"] = ["Ctrl+Shift+A"],
            ["span.selection.invert"]     = ["Ctrl+I"],

            // View
            ["span.view.miller"]           = ["Ctrl+1"],
            ["span.view.details"]          = ["Ctrl+2"],
            ["span.view.list"]             = ["Ctrl+3"],
            ["span.view.icon"]             = ["Ctrl+4"],
            ["span.view.splitView"]        = ["Ctrl+Shift+E"],
            ["span.view.preview"]          = ["Ctrl+Shift+P"],
            ["span.view.equalizeColumns"]  = ["Ctrl+Shift+Plus"],
            ["span.view.autoFitColumns"]   = ["Ctrl+Shift+Minus"],
            ["span.view.refresh"]          = ["F5"],
            ["span.view.toggleHidden"]     = ["Ctrl+H"],
            ["span.view.fullscreen"]       = ["F11"],

            // Tab
            ["span.tab.new"]              = ["Ctrl+T"],
            ["span.tab.close"]            = ["Ctrl+W"],
            ["span.tab.openSelectedInNew"] = ["Ctrl+Enter"],

            // Window
            ["span.window.new"]        = ["Ctrl+N"],
            ["span.window.terminal"]   = ["Ctrl+`", "Ctrl+'"],
            ["span.window.settings"]   = ["Ctrl+,"],
            ["span.window.properties"] = ["Alt+Enter"],
            ["span.window.help"]       = ["F1", "Shift+/"],

            // Quick Look
            ["span.quickLook.toggle"] = ["Space"],
        };
    }

    // ── Private: JSON 직렬화 ──────────────────────────────────

    private void LoadAndMerge()
    {
        // 기본 바인딩으로 시작
        _currentBindings = GetDefaultBindings();

        // 사용자 오버라이드 로드
        var json = _settingsService.Get<string>(SettingsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            RebuildReverseIndex();
            return;
        }

        try
        {
            var overrideData = JsonSerializer.Deserialize<KeyBindingsJson>(json, _jsonOptions);
            if (overrideData?.Overrides is { Count: > 0 })
            {
                foreach (var entry in overrideData.Overrides)
                {
                    if (string.IsNullOrWhiteSpace(entry.Command))
                        continue;

                    if (!_currentBindings.ContainsKey(entry.Command))
                        continue; // 알 수 없는 커맨드 무시

                    // 오버라이드 적용 (빈 배열 = 바인딩 제거)
                    _currentBindings[entry.Command] = entry.Keys != null
                        ? new List<string>(entry.Keys)
                        : new List<string>();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[KeyBindingService] JSON parse error, using defaults: {ex.Message}");
            _currentBindings = GetDefaultBindings();

            // 손상된 JSON 제거
            try { _settingsService.Set<string>(SettingsKey, string.Empty); }
            catch { /* best effort */ }
        }

        RebuildReverseIndex();
    }

    private void SaveOverrides()
    {
        try
        {
            var overrides = new List<KeyBindingOverrideEntry>();

            foreach (var (commandId, currentKeys) in _currentBindings)
            {
                if (!_defaultBindings.TryGetValue(commandId, out var defaultKeys))
                    continue;

                // 기본과 다를 때만 오버라이드로 저장
                if (!KeyListsEqual(currentKeys, defaultKeys))
                {
                    overrides.Add(new KeyBindingOverrideEntry
                    {
                        Command = commandId,
                        Keys = currentKeys
                    });
                }
            }

            if (overrides.Count == 0)
            {
                // 모두 기본 → JSON 제거
                _settingsService.Set<string>(SettingsKey, string.Empty);
                return;
            }

            var data = new KeyBindingsJson
            {
                Version = CurrentVersion,
                Overrides = overrides
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            _settingsService.Set(SettingsKey, json);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[KeyBindingService] Error saving overrides: {ex.Message}");
        }
    }

    // ── Private: 역인덱스 ─────────────────────────────────────

    private void RebuildReverseIndex()
    {
        var newIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (commandId, keys) in _currentBindings)
        {
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                // 중복 키는 먼저 등록된 커맨드가 우선
                if (!newIndex.ContainsKey(key))
                    newIndex[key] = commandId;
                else
                    DebugLogger.Log($"[KeyBindingService] Duplicate key '{key}': '{commandId}' ignored, already mapped to '{newIndex[key]}'");
            }
        }

        _reverseIndex = newIndex;
    }

    // ── Private: 유틸리티 ─────────────────────────────────────

    private static string BuildKeyStringRaw(bool ctrl, bool shift, bool alt, string keyName)
    {
        var parts = new List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static string VirtualKeyToString(VirtualKey key)
    {
        return key switch
        {
            // 숫자키
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",

            // 기능키
            VirtualKey.F1  => "F1",
            VirtualKey.F2  => "F2",
            VirtualKey.F3  => "F3",
            VirtualKey.F4  => "F4",
            VirtualKey.F5  => "F5",
            VirtualKey.F6  => "F6",
            VirtualKey.F7  => "F7",
            VirtualKey.F8  => "F8",
            VirtualKey.F9  => "F9",
            VirtualKey.F10 => "F10",
            VirtualKey.F11 => "F11",
            VirtualKey.F12 => "F12",

            // 특수키
            VirtualKey.Space   => "Space",
            VirtualKey.Enter   => "Enter",
            VirtualKey.Escape  => "Escape",
            VirtualKey.Back    => "Back",
            VirtualKey.Tab     => "Tab",
            VirtualKey.Delete  => "Delete",
            VirtualKey.Home    => "Home",
            VirtualKey.End     => "End",
            VirtualKey.Left    => "Left",
            VirtualKey.Right   => "Right",
            VirtualKey.Up      => "Up",
            VirtualKey.Down    => "Down",
            VirtualKey.PageUp  => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert  => "Insert",

            // OEM 키 (사용자 친화적 이름)
            (VirtualKey)186 => ";",
            (VirtualKey)187 => "=",
            (VirtualKey)188 => ",",
            (VirtualKey)189 => "-",
            (VirtualKey)190 => ".",
            (VirtualKey)191 => "/",
            (VirtualKey)192 => "`",
            (VirtualKey)219 => "[",
            (VirtualKey)220 => "\\",
            (VirtualKey)221 => "]",
            (VirtualKey)222 => "'",

            // Snapshot / PrintScreen
            VirtualKey.Snapshot => "PrintScreen",

            // 알파벳
            _ => key.ToString()
        };
    }

    private static bool KeyListsEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // ── JSON 모델 ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed class KeyBindingsJson
    {
        public int Version { get; set; } = CurrentVersion;
        public List<KeyBindingOverrideEntry> Overrides { get; set; } = [];
    }

    private sealed class KeyBindingOverrideEntry
    {
        public string Command { get; set; } = string.Empty;
        public List<string> Keys { get; set; } = [];
    }
}

// ── 충돌 결과 ─────────────────────────────────────────────────

public enum ConflictType
{
    None,
    AlreadyAssigned,
    SystemReserved,
    Structural
}

public class ConflictResult
{
    public ConflictType Type { get; set; }
    public string? ExistingCommandId { get; set; }
    public string? ExistingCommandName { get; set; }
    public string? Message { get; set; }

    public static ConflictResult NoConflict => new() { Type = ConflictType.None };
}
