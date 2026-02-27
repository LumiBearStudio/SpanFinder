using System;

namespace Span.Models
{
    /// <summary>
    /// 파일 시스템 항목(파일/폴더)의 공통 인터페이스.
    /// <see cref="FileItem"/>, <see cref="FolderItem"/>이 구현하며,
    /// Miller Columns 및 모든 뷰 모드에서 통합 컬렉션으로 바인딩된다.
    /// </summary>
    public interface IFileSystemItem
    {
        /// <summary>파일 또는 폴더 이름 (확장자 포함).</summary>
        string Name { get; set; }

        /// <summary>전체 경로 (예: "C:\Users\Dev\file.txt").</summary>
        string Path { get; set; }

        /// <summary>현재 아이콘 팩에 따른 아이콘 글리프 문자. IconService를 통해 런타임에 결정된다.</summary>
        string IconGlyph { get; }

        /// <summary>숨김 파일/폴더 여부. 설정의 "숨김 파일 표시" 옵션에 의해 필터링된다.</summary>
        bool IsHidden { get; set; }
    }
}
