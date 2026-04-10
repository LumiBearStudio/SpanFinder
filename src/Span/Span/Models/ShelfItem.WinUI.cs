using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// ShelfItem의 WinUI 의존 멤버.
    /// 단위 테스트 프로젝트(Span.Tests)는 ShelfItem.cs만 링크하기 때문에
    /// WinUI 타입(Brush 등)이 필요한 멤버는 이 partial 파일에 격리한다.
    /// </summary>
    public partial class ShelfItem
    {
        /// <summary>아이콘 색상 브러시 (Sidebar/Shelf UI 표시용).</summary>
        public Brush IconBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
