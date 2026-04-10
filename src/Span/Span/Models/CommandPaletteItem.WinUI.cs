using Microsoft.UI.Xaml;

namespace Span.Models
{
    /// <summary>
    /// CommandPaletteItem의 WinUI 의존 멤버.
    /// 단위 테스트 프로젝트(Span.Tests)는 CommandPaletteItem.cs만 링크하기 때문에
    /// WinUI 타입(Visibility 등)이 필요한 멤버는 이 partial 파일에 격리한다.
    /// </summary>
    public partial class CommandPaletteItem
    {
        // x:Bind용 (실제 opacity는 별도). 항상 Visible 반환은 의도된 동작 — IsEnabled에 따라
        // Opacity 프로퍼티만 바뀌고 항목 자체는 계속 표시된다.
        public Visibility ItemOpacity => IsEnabled ? Visibility.Visible : Visibility.Visible;
    }
}
