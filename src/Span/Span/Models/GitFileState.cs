namespace Span.Models
{
    /// <summary>
    /// Git 파일/폴더 상태.
    /// </summary>
    public enum GitFileState
    {
        /// <summary>Git 레포가 아님</summary>
        None,

        /// <summary>추적 중, 변경 없음</summary>
        Clean,

        /// <summary>M — 수정됨</summary>
        Modified,

        /// <summary>A — 스테이징 추가됨</summary>
        Added,

        /// <summary>D — 삭제됨</summary>
        Deleted,

        /// <summary>R — 이름 변경됨</summary>
        Renamed,

        /// <summary>? — 미추적 파일</summary>
        Untracked,

        /// <summary>U — 병합 충돌</summary>
        Conflicted,

        /// <summary>! — .gitignore에 의해 무시됨</summary>
        Ignored,
    }
}
