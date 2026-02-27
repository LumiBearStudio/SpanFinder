using System;

namespace Span.Models
{
    /// <summary>
    /// 단일 파일을 나타내는 데이터 모델.
    /// <see cref="IFileSystemItem"/>을 구현하며, FolderViewModel.Children 컬렉션에 포함된다.
    /// </summary>
    public class FileItem : IFileSystemItem
    {
        /// <summary>파일명 (확장자 포함).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>파일 전체 경로.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>파일 크기 (바이트 단위).</summary>
        public long Size { get; set; }

        /// <summary>마지막 수정 시각.</summary>
        public DateTime DateModified { get; set; }

        /// <summary>현재 아이콘 팩의 기본 파일 아이콘 글리프. IconService.Current를 통해 런타임에 결정된다.</summary>
        public string IconGlyph => Span.Services.IconService.Current?.FileDefaultGlyph ?? "\uECE0";

        /// <summary>파일 타입 설명 문자열 (예: "Text Document", "PNG Image").</summary>
        public string FileType { get; set; } = string.Empty;

        /// <summary>숨김 파일 여부.</summary>
        public bool IsHidden { get; set; }
    }
}
