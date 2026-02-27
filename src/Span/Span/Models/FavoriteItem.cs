using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// 사이드바 즐겨찾기 항목. FavoritesService가 JSON으로 영속화하며,
    /// 드래그 드롭 또는 컨텍스트 메뉴로 추가/제거/순서 변경할 수 있다.
    /// </summary>
    public class FavoriteItem
    {
        /// <summary>표시 이름.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>폴더 전체 경로.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>아이콘 글리프 문자 (현재 아이콘 팩 기준).</summary>
        public string IconGlyph { get; set; } = string.Empty;

        /// <summary>아이콘 색상 (HEX, 예: "#FFC857").</summary>
        public string IconColor { get; set; } = "#FFFFFF";

        /// <summary>정렬 순서 (드래그로 변경 가능).</summary>
        public int Order { get; set; }

        /// <summary>
        /// IconColor를 파싱하여 SolidColorBrush를 반환. XAML 바인딩용.
        /// 파싱 실패 시 흰색 브러시를 반환한다.
        /// </summary>
        public SolidColorBrush IconBrush
        {
            get
            {
                try
                {
                    var hex = IconColor.TrimStart('#');
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
                }
                catch
                {
                    return new SolidColorBrush(Colors.White);
                }
            }
        }
    }
}
