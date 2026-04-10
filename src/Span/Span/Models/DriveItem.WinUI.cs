using Microsoft.UI.Xaml;

namespace Span.Models
{
    /// <summary>
    /// DriveItem의 WinUI 의존 멤버.
    /// 단위 테스트 프로젝트(Span.Tests)는 DriveItem.cs만 링크하기 때문에
    /// WinUI 타입(Visibility 등)이 필요한 멤버는 이 partial 파일에 격리한다.
    /// </summary>
    public partial class DriveItem
    {
        /// <summary>잠금 뱃지 Visibility (x:Bind용)</summary>
        public Visibility AuthBadgeVisibility =>
            NeedsAuth ? Visibility.Visible : Visibility.Collapsed;
    }
}
