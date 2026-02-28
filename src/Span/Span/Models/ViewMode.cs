namespace Span.Models
{
    /// <summary>
    /// 파일 탐색기 뷰 모드 정의
    /// </summary>
    public enum ViewMode
    {
        /// <summary>
        /// Miller Columns: macOS Finder 스타일 계층 탐색
        /// </summary>
        MillerColumns = 0,

        /// <summary>
        /// Details: 테이블 뷰 (Name, Date Modified, Type, Size)
        /// </summary>
        Details = 1,

        /// <summary>
        /// Icon Small: 16x16 그리드
        /// </summary>
        IconSmall = 2,

        /// <summary>
        /// Icon Medium: 48x48 그리드
        /// </summary>
        IconMedium = 3,

        /// <summary>
        /// Icon Large: 96x96 그리드
        /// </summary>
        IconLarge = 4,

        /// <summary>
        /// Icon Extra Large: 256x256 그리드 (썸네일 지원)
        /// </summary>
        IconExtraLarge = 5,

        /// <summary>
        /// Home: 즐겨찾기 및 빠른 액세스 홈 화면
        /// </summary>
        Home = 6,

        /// <summary>
        /// Settings: 임베디드 설정 페이지
        /// </summary>
        Settings = 7,

        /// <summary>
        /// List: 고밀도 멀티컬럼 리스트 (세로 흐름, Windows Explorer List 스타일)
        /// </summary>
        List = 8,

        /// <summary>
        /// ActionLog: 파일 작업 로그 뷰
        /// </summary>
        ActionLog = 9
    }
}
