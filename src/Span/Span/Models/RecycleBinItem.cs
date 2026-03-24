using System;

namespace Span.Models
{
    /// <summary>
    /// 휴지통 항목 모델. IFileSystemItem을 구현하여 기존 뷰 바인딩과 호환.
    /// Shell.Application COM의 GetDetailsOf() 결과를 매핑한다.
    /// </summary>
    public class RecycleBinItem : IFileSystemItem
    {
        /// <summary>파일/폴더 이름 (확장자 포함).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 휴지통 내 실제 물리적 경로 ($Recycle.Bin\SID\$R...).
        /// 미리보기, 아이콘 로드, 영구 삭제 시 사용.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>삭제 전 원래 전체 경로 (디렉토리 + 파일명).</summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>삭제 전 원래 디렉토리 경로 (GetDetailsOf column 1).</summary>
        public string OriginalLocation { get; set; } = string.Empty;

        /// <summary>삭제된 날짜/시간 (GetDetailsOf column 2).</summary>
        public DateTime DateDeleted { get; set; }

        /// <summary>마지막 수정 시각 (삭제 전, GetDetailsOf column 5).</summary>
        public DateTime DateModified { get; set; }

        /// <summary>파일 크기 (바이트). 폴더는 0.</summary>
        public long Size { get; set; }

        /// <summary>파일/폴더 종류 설명 (GetDetailsOf column 4).</summary>
        public string ItemType { get; set; } = string.Empty;

        /// <summary>폴더 여부.</summary>
        public bool IsFolder { get; set; }

        /// <summary>아이콘 글리프 (폴더/파일 기본 아이콘).</summary>
        public string IconGlyph => IsFolder
            ? (Span.Services.IconService.Current?.FolderGlyph ?? "\uED53")
            : (Span.Services.IconService.Current?.FileDefaultGlyph ?? "\uECE0");

        /// <summary>숨김 파일/폴더 여부.</summary>
        public bool IsHidden { get; set; }
    }
}
