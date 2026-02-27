namespace Span.Models
{
    /// <summary>
    /// 네트워크 브라우저에서 탐색되는 항목의 유형.
    /// </summary>
    public enum NetworkItemType
    {
        /// <summary>네트워크 서버 (컴퓨터).</summary>
        Server,
        /// <summary>공유 폴더.</summary>
        Share
    }

    /// <summary>
    /// SMB 네트워크 브라우저에서 발견된 서버 또는 공유 폴더 항목.
    /// <see cref="Services.NetworkBrowserService"/>에 의해 WNetEnumResourceW P/Invoke로 열거된다.
    /// </summary>
    public class NetworkItem
    {
        /// <summary>서버명 또는 공유 폴더명.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>UNC 경로 (예: "\\server" 또는 "\\server\share").</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>항목 유형 (서버 또는 공유).</summary>
        public NetworkItemType Type { get; set; }

        /// <summary>아이콘 글리프 (런타임에 IconService가 재설정).</summary>
        public string IconGlyph { get; set; } = "\uEDD4";

        /// <summary>서버/공유의 코멘트 (NetShareEnum의 shi1_remark).</summary>
        public string Comment { get; set; } = string.Empty;
    }
}
