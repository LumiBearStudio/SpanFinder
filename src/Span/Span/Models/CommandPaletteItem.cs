using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace Span.Models
{
    public enum CommandPaletteItemType
    {
        Command,
        Tab,
        Navigation,
        SettingToggle,    // boolean 토글 (즉시 적용)
        SettingSelect,    // 선택형 (예: Theme: Dark)
        SettingsSection,  // Settings 섹션 점프
    }

    /// <summary>
    /// Command Palette에 표시되는 개별 항목.
    /// </summary>
    public class CommandPaletteItem
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;  // ListView GroupStyle용
        public string IconGlyph { get; set; } = string.Empty;
        public string Shortcut { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int TabIndex { get; set; } = -1;
        public CommandPaletteItemType Type { get; set; }

        // Settings 명령용
        public string SettingKey { get; set; } = string.Empty;     // 예: "ShowHiddenFiles"
        public object? SettingValue { get; set; }                  // 적용할 값
        public string CurrentStateText { get; set; } = string.Empty; // 예: "ON" / "OFF"

        // 검색용 별칭 (한글/영문)
        public List<string> Aliases { get; } = new();

        // 컨텍스트 활성화
        public bool IsEnabled { get; set; } = true;
        public Visibility ItemOpacity => IsEnabled ? Visibility.Visible : Visibility.Visible; // x:Bind용 (실제 opacity는 별도)
        public double Opacity => IsEnabled ? 1.0 : 0.4;
    }

    /// <summary>
    /// ListView GroupStyle용 그룹 컨테이너.
    /// </summary>
    public class CommandPaletteGroup : List<CommandPaletteItem>
    {
        public string Key { get; set; } = string.Empty;

        public CommandPaletteGroup(string key, IEnumerable<CommandPaletteItem> items) : base(items)
        {
            Key = key;
        }
    }
}
