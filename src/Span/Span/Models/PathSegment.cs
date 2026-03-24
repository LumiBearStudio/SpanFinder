using Microsoft.UI.Xaml;

namespace Span.Models
{
    /// <summary>
    /// 브레드크럼 주소 표시줄의 각 세그먼트 (예: "C:", "Users", "Dev").
    /// </summary>
    public class PathSegment
    {
        public string Name { get; }
        public string FullPath { get; }
        public bool IsLast { get; }
        /// <summary>세그먼트 앞에 표시할 아이콘 글리프 (null이면 아이콘 없음).</summary>
        public string? IconGlyph { get; }
        /// <summary>아이콘 글리프의 폰트 패밀리 (null이면 기본 Segoe MDL2).</summary>
        public string? IconFontFamily { get; }

        /// <summary>
        /// 마지막 세그먼트이면 Collapsed, 아니면 Visible (chevron 표시용).
        /// </summary>
        public Visibility ChevronVisibility => IsLast ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>아이콘이 있으면 Visible.</summary>
        public Visibility IconVisibility => string.IsNullOrEmpty(IconGlyph) ? Visibility.Collapsed : Visibility.Visible;

        public PathSegment(string name, string fullPath, bool isLast = false,
            string? iconGlyph = null, string? iconFontFamily = null)
        {
            Name = name;
            FullPath = fullPath;
            IsLast = isLast;
            IconGlyph = iconGlyph;
            IconFontFamily = iconFontFamily;
        }
    }
}
