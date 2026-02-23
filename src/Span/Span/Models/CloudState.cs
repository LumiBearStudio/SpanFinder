namespace Span.Models
{
    /// <summary>
    /// 클라우드 동기화 상태 (OneDrive 등).
    /// </summary>
    public enum CloudState
    {
        /// <summary>클라우드 폴더가 아님 또는 상태 확인 불가</summary>
        None,

        /// <summary>로컬에 완전히 동기화됨 (녹색 체크)</summary>
        Synced,

        /// <summary>클라우드에만 존재 (파란 구름)</summary>
        CloudOnly,

        /// <summary>업로드 대기 중</summary>
        PendingUpload,

        /// <summary>동기화 진행 중</summary>
        Syncing,
    }
}
