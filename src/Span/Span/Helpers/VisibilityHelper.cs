using Microsoft.UI.Xaml;

namespace Span.Helpers
{
    /// <summary>
    /// x:Bind 함수 바인딩용 Visibility 변환 헬퍼.
    /// </summary>
    internal static class VisibilityHelper
    {
        /// <summary>
        /// 문자열이 비어있지 않으면 Visible, 비어있으면 Collapsed.
        /// </summary>
        public static Visibility FromNonEmpty(string? value) =>
            string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
