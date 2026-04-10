using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// ShelfService의 WinUI 의존 부분.
    /// IconBrush(Microsoft.UI.Xaml.Media.Brush) 부착을 담당한다.
    /// 단위 테스트 프로젝트(Span.Tests)는 이 파일을 링크하지 않으므로
    /// 테스트 빌드에서는 ApplyVisualBrush가 no-op partial 메서드로 자동 제거된다.
    /// </summary>
    public partial class ShelfService
    {
        partial void ApplyVisualBrush(ShelfItem item, bool isDir, string ext)
        {
            item.IconBrush = isDir ? _iconService.FolderBrush : _iconService.GetBrush(ext);
        }
    }
}
