namespace Span.Models;

/// <summary>
/// Issue #58: 폴더 컬러 태그. macOS Finder의 컬러 라벨에 대응한다.
/// 값은 desktop.ini에 문자열(enum 이름)로 저장되므로 이름을 바꾸면 기존 태그가 깨진다.
/// </summary>
public enum FolderTagColor
{
    None = 0,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Gray,
}
