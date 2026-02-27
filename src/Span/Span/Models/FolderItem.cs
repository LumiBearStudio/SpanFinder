using System;
using System.Collections.Generic;

namespace Span.Models
{
    /// <summary>
    /// 폴더를 나타내는 데이터 모델.
    /// <see cref="IFileSystemItem"/>을 구현하며, 하위 파일과 폴더를 Children 컬렉션으로 통합한다.
    /// </summary>
    public class FolderItem : IFileSystemItem
    {
        /// <summary>폴더명.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>폴더 전체 경로.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>마지막 수정 시각.</summary>
        public DateTime DateModified { get; set; }

        /// <summary>하위 파일 목록 (구조적 접근용).</summary>
        public List<FileItem> Files { get; set; } = new List<FileItem>();

        /// <summary>하위 폴더 목록 (구조적 접근용).</summary>
        public List<FolderItem> SubFolders { get; set; } = new List<FolderItem>();

        /// <summary>
        /// UI 바인딩용 통합 컬렉션. 파일과 폴더를 IFileSystemItem으로 합쳐서 ListView/ItemsControl에 바인딩한다.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<IFileSystemItem> Children { get; set; } = new();

        /// <summary>숨김 폴더 여부.</summary>
        public bool IsHidden { get; set; }

        /// <summary>현재 아이콘 팩의 폴더 아이콘 글리프. IconService.Current를 통해 런타임에 결정된다.</summary>
        public string IconGlyph => Span.Services.IconService.Current?.FolderGlyph ?? "\uED53";
    }
}
