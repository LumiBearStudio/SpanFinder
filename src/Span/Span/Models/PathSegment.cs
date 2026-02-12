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

        /// <summary>
        /// 마지막 세그먼트이면 Collapsed, 아니면 Visible (chevron 표시용).
        /// </summary>
        public Visibility ChevronVisibility => IsLast ? Visibility.Collapsed : Visibility.Visible;

        public PathSegment(string name, string fullPath, bool isLast = false)
        {
            Name = name;
            FullPath = fullPath;
            IsLast = isLast;
        }
    }
}
