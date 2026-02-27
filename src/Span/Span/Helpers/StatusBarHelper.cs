using Microsoft.UI.Xaml;

namespace Span.Helpers
{
    /// <summary>
    /// 상태 표시줄 XAML 바인딩용 정적 헬퍼.
    /// </summary>
    public static class StatusBarHelper
    {
        /// <summary>텍스트가 비어있지 않으면 Visible, 비어있으면 Collapsed.</summary>
        public static Visibility ShowIfNotEmpty(string text)
            => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
