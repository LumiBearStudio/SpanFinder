using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;

namespace Span.Helpers
{
    public static class ViewModeExtensions
    {
        /// <summary>
        /// Resolve LocalizationService via DI for static helper access.
        /// </summary>
        private static string Loc(string key, string fallback)
        {
            try
            {
                return App.Current.Services.GetService<LocalizationService>()?.Get(key) ?? fallback;
            }
            catch { return fallback; }
        }

        /// <summary>
        /// ViewMode가 Icon 계열인지 확인
        /// </summary>
        public static bool IsIconMode(this ViewMode mode)
        {
            return mode >= ViewMode.IconSmall && mode <= ViewMode.IconExtraLarge && mode != ViewMode.Home && mode != ViewMode.Settings;
        }

        /// <summary>
        /// Icon 모드의 픽셀 크기 반환
        /// </summary>
        public static int GetIconPixelSize(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.IconSmall => 16,
                ViewMode.IconMedium => 48,
                ViewMode.IconLarge => 96,
                ViewMode.IconExtraLarge => 256,
                _ => 48 // Default
            };
        }

        /// <summary>
        /// ViewMode 표시 이름 (UI용)
        /// </summary>
        public static string GetDisplayName(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.MillerColumns => Loc("MillerColumns", "Miller Columns"),
                ViewMode.Details => Loc("Details", "Details"),
                ViewMode.IconSmall => Loc("SmallIcons", "Small Icons"),
                ViewMode.IconMedium => Loc("MediumIcons", "Medium Icons"),
                ViewMode.IconLarge => Loc("LargeIcons", "Large Icons"),
                ViewMode.IconExtraLarge => Loc("ExtraLargeIcons", "Extra Large Icons"),
                ViewMode.Home => Loc("Home", "Home"),
                ViewMode.Settings => Loc("Settings", "Settings"),
                ViewMode.List => Loc("ViewMode_List", "List"),
                _ => mode.ToString()
            };
        }

        /// <summary>
        /// 키보드 단축키 텍스트
        /// </summary>
        public static string GetShortcutText(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.MillerColumns => "Ctrl+1",
                ViewMode.Details => "Ctrl+2",
                ViewMode.List => "Ctrl+3",
                ViewMode.IconSmall or ViewMode.IconMedium or ViewMode.IconLarge or ViewMode.IconExtraLarge => "Ctrl+4",
                ViewMode.Home => "",
                _ => ""
            };
        }
    }
}
