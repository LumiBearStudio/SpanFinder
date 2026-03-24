using System;
using System.Collections.Generic;
using System.Text;
using WinVirtualKey = Windows.System.VirtualKey;

namespace Span.Models
{
    /// <summary>
    /// 키보드 단축키와 커맨드 ID 간의 매핑 모델.
    /// 설정 저장/로드 및 런타임 키 매칭에 사용됩니다.
    /// </summary>
    public class KeyBinding : IEquatable<KeyBinding>
    {
        /// <summary>
        /// 표시 및 저장용 키 문자열 (예: "Ctrl+C", "Ctrl+Shift+N")
        /// </summary>
        public string KeyString { get; set; } = string.Empty;

        /// <summary>
        /// Windows VirtualKey 정수값 (예: 67 = C, 116 = F5)
        /// </summary>
        public int VirtualKey { get; set; }

        /// <summary>
        /// 선택적 스캔 코드 fallback. 한국어/일본어 IME에서 OEM 키 매핑이 다를 때 사용.
        /// 0이면 스캔 코드 매칭을 건너뜁니다.
        /// </summary>
        public int ScanCode { get; set; }

        /// <summary>
        /// Ctrl 수식 키 여부
        /// </summary>
        public bool Ctrl { get; set; }

        /// <summary>
        /// Shift 수식 키 여부
        /// </summary>
        public bool Shift { get; set; }

        /// <summary>
        /// Alt 수식 키 여부
        /// </summary>
        public bool Alt { get; set; }

        /// <summary>
        /// 이 키 바인딩이 실행하는 커맨드 ID (예: "span.edit.copy")
        /// </summary>
        public string CommandId { get; set; } = string.Empty;

        // ── VirtualKey 이름 <-> 값 매핑 ──────────────────────────

        private static readonly Dictionary<string, WinVirtualKey> _keyNameToVirtualKey = new(StringComparer.OrdinalIgnoreCase)
        {
            // 알파벳
            { "A", WinVirtualKey.A }, { "B", WinVirtualKey.B }, { "C", WinVirtualKey.C },
            { "D", WinVirtualKey.D }, { "E", WinVirtualKey.E }, { "F", WinVirtualKey.F },
            { "G", WinVirtualKey.G }, { "H", WinVirtualKey.H }, { "I", WinVirtualKey.I },
            { "J", WinVirtualKey.J }, { "K", WinVirtualKey.K }, { "L", WinVirtualKey.L },
            { "M", WinVirtualKey.M }, { "N", WinVirtualKey.N }, { "O", WinVirtualKey.O },
            { "P", WinVirtualKey.P }, { "Q", WinVirtualKey.Q }, { "R", WinVirtualKey.R },
            { "S", WinVirtualKey.S }, { "T", WinVirtualKey.T }, { "U", WinVirtualKey.U },
            { "V", WinVirtualKey.V }, { "W", WinVirtualKey.W }, { "X", WinVirtualKey.X },
            { "Y", WinVirtualKey.Y }, { "Z", WinVirtualKey.Z },
            // 숫자
            { "0", WinVirtualKey.Number0 }, { "1", WinVirtualKey.Number1 },
            { "2", WinVirtualKey.Number2 }, { "3", WinVirtualKey.Number3 },
            { "4", WinVirtualKey.Number4 }, { "5", WinVirtualKey.Number5 },
            { "6", WinVirtualKey.Number6 }, { "7", WinVirtualKey.Number7 },
            { "8", WinVirtualKey.Number8 }, { "9", WinVirtualKey.Number9 },
            // 펑션 키
            { "F1", WinVirtualKey.F1 }, { "F2", WinVirtualKey.F2 },
            { "F3", WinVirtualKey.F3 }, { "F4", WinVirtualKey.F4 },
            { "F5", WinVirtualKey.F5 }, { "F6", WinVirtualKey.F6 },
            { "F7", WinVirtualKey.F7 }, { "F8", WinVirtualKey.F8 },
            { "F9", WinVirtualKey.F9 }, { "F10", WinVirtualKey.F10 },
            { "F11", WinVirtualKey.F11 }, { "F12", WinVirtualKey.F12 },
            // 특수 키
            { "Enter", WinVirtualKey.Enter }, { "Return", WinVirtualKey.Enter },
            { "Escape", WinVirtualKey.Escape }, { "Esc", WinVirtualKey.Escape },
            { "Space", WinVirtualKey.Space },
            { "Tab", WinVirtualKey.Tab },
            { "Backspace", WinVirtualKey.Back }, { "Back", WinVirtualKey.Back },
            { "Delete", WinVirtualKey.Delete }, { "Del", WinVirtualKey.Delete },
            { "Insert", WinVirtualKey.Insert }, { "Ins", WinVirtualKey.Insert },
            { "Home", WinVirtualKey.Home }, { "End", WinVirtualKey.End },
            { "PageUp", WinVirtualKey.PageUp }, { "PageDown", WinVirtualKey.PageDown },
            // 방향 키
            { "Left", WinVirtualKey.Left }, { "Right", WinVirtualKey.Right },
            { "Up", WinVirtualKey.Up }, { "Down", WinVirtualKey.Down },
            // OEM 키 (VK 코드 직접 매핑)
            { ",", (WinVirtualKey)188 },       // VK_OEM_COMMA
            { ".", (WinVirtualKey)190 },       // VK_OEM_PERIOD
            { ";", (WinVirtualKey)186 },       // VK_OEM_1
            { "/", (WinVirtualKey)191 },       // VK_OEM_2
            { "`", (WinVirtualKey)192 },       // VK_OEM_3
            { "[", (WinVirtualKey)219 },       // VK_OEM_4
            { "\\", (WinVirtualKey)220 },      // VK_OEM_5
            { "]", (WinVirtualKey)221 },       // VK_OEM_6
            { "'", (WinVirtualKey)222 },       // VK_OEM_7
            { "-", (WinVirtualKey)189 },       // VK_OEM_MINUS
            { "=", (WinVirtualKey)187 },       // VK_OEM_PLUS (= key)
        };

        private static readonly Dictionary<int, string> _virtualKeyToName;

        static KeyBinding()
        {
            // 역방향 매핑 생성 (동일 VirtualKey에 여러 이름이 있으면 첫 번째 것 사용)
            _virtualKeyToName = new Dictionary<int, string>();
            foreach (var kvp in _keyNameToVirtualKey)
            {
                var vk = (int)kvp.Value;
                if (!_virtualKeyToName.ContainsKey(vk))
                {
                    _virtualKeyToName[vk] = kvp.Key;
                }
            }
        }

        /// <summary>
        /// "Ctrl+Shift+N" 같은 키 문자열을 파싱하여 KeyBinding 객체를 생성합니다.
        /// </summary>
        /// <param name="keyString">파싱할 키 문자열</param>
        /// <param name="commandId">연결할 커맨드 ID (선택)</param>
        /// <returns>파싱된 KeyBinding. 파싱 실패 시 VirtualKey=0인 객체 반환.</returns>
        public static KeyBinding FromKeyString(string keyString, string commandId = "")
        {
            var binding = new KeyBinding
            {
                KeyString = keyString,
                CommandId = commandId,
            };

            if (string.IsNullOrWhiteSpace(keyString))
                return binding;

            var parts = keyString.Split('+');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                var upper = trimmed.ToUpperInvariant();

                if (upper == "CTRL" || upper == "CONTROL")
                {
                    binding.Ctrl = true;
                }
                else if (upper == "SHIFT")
                {
                    binding.Shift = true;
                }
                else if (upper == "ALT")
                {
                    binding.Alt = true;
                }
                else
                {
                    // 메인 키 — 원래 대소문자로 룩업 (OEM 문자 등)
                    if (_keyNameToVirtualKey.TryGetValue(trimmed, out var vk))
                    {
                        binding.VirtualKey = (int)vk;
                    }
                }
            }

            return binding;
        }

        /// <summary>
        /// 수식 키와 VirtualKey 조합으로 표시용 키 문자열을 생성합니다.
        /// 순서: Ctrl -> Shift -> Alt -> Key
        /// </summary>
        public static string BuildKeyString(bool ctrl, bool shift, bool alt, WinVirtualKey key)
        {
            var sb = new StringBuilder();

            if (ctrl)
                sb.Append("Ctrl+");
            if (shift)
                sb.Append("Shift+");
            if (alt)
                sb.Append("Alt+");

            if (_virtualKeyToName.TryGetValue((int)key, out var keyName))
            {
                sb.Append(keyName);
            }
            else
            {
                // Enum 이름 fallback
                sb.Append(key.ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// 수식 키와 VirtualKey 정수값 조합으로 표시용 키 문자열을 생성합니다.
        /// </summary>
        public static string BuildKeyString(bool ctrl, bool shift, bool alt, int virtualKeyCode)
        {
            return BuildKeyString(ctrl, shift, alt, (WinVirtualKey)virtualKeyCode);
        }

        /// <summary>
        /// 주어진 키 이벤트가 이 바인딩과 일치하는지 확인합니다.
        /// VirtualKey 우선 매칭, 실패 시 ScanCode fallback.
        /// </summary>
        /// <param name="ctrl">Ctrl 눌림 여부</param>
        /// <param name="shift">Shift 눌림 여부</param>
        /// <param name="alt">Alt 눌림 여부</param>
        /// <param name="virtualKey">이벤트 VirtualKey 값</param>
        /// <param name="scanCode">이벤트 ScanCode 값 (0이면 스캔 코드 매칭 건너뜀)</param>
        /// <returns>일치 여부</returns>
        public bool Matches(bool ctrl, bool shift, bool alt, int virtualKey, int scanCode = 0)
        {
            if (Ctrl != ctrl || Shift != shift || Alt != alt)
                return false;

            // VirtualKey 매칭
            if (VirtualKey != 0 && VirtualKey == virtualKey)
                return true;

            // ScanCode fallback (한국어/일본어 IME 대응)
            if (ScanCode != 0 && scanCode != 0 && ScanCode == scanCode)
                return true;

            return false;
        }

        // ── Equals / GetHashCode ────────────────────────────────

        public override bool Equals(object? obj)
        {
            return Equals(obj as KeyBinding);
        }

        public bool Equals(KeyBinding? other)
        {
            if (other is null)
                return false;

            return Ctrl == other.Ctrl
                && Shift == other.Shift
                && Alt == other.Alt
                && VirtualKey == other.VirtualKey;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Ctrl, Shift, Alt, VirtualKey);
        }

        public static bool operator ==(KeyBinding left, KeyBinding right)
        {
            if (left is null)
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(KeyBinding left, KeyBinding right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(KeyString)
                ? BuildKeyString(Ctrl, Shift, Alt, VirtualKey)
                : KeyString;
        }
    }
}
