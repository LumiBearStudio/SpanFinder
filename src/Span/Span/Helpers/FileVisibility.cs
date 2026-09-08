using System.IO;

namespace Span.Helpers;

/// <summary>
/// 파일·폴더를 목록에 표시할지 판단하는 단일 규칙 (Issue #68).
///
/// Windows 탐색기의 규칙은 셋으로 갈린다:
///   Hidden 단독      → "숨김 항목" 옵션을 켜야 표시
///   Hidden + System  → "보호된 운영 체제 파일 숨기기"까지 꺼야 표시
///   System 단독      → <b>항상 표시</b>
///
/// 폴더에 System을 다는 건 숨기라는 뜻이 아니라 desktop.ini 기반 커스텀 아이콘·표시
/// 이름을 켜는 표준 관용구다. 네이버 MYBOX 드라이브 루트(개인/공유/즐겨찾기/휴지통),
/// C:\ProgramData\Microsoft, C:\Windows\Fonts 가 모두 System 단독이다.
/// 그래서 System은 판단에 넣지 않는다 — 넣었더니 MYBOX 드라이브가 기본 설정에서
/// 통째로 빈 폴더로 보였다(Issue #68).
///
/// SPAN은 탐색기의 두 토글을 ShowHiddenFiles 하나로 합쳐 쓰므로,
/// Hidden+System 항목은 그 토글 하나로 표시된다(탐색기보다 관대).
///
/// 순수 함수 — I/O 없음, UI 스레드에서 호출해도 안전.
/// </summary>
internal static class FileVisibility
{
    /// <summary>
    /// 속성만 보고 "숨김 항목"인지 판단. System 단독은 숨김으로 보지 않는다.
    /// 설정을 반영해야 하면 <see cref="ShouldHide"/>를 써라.
    /// </summary>
    public static bool IsHidden(FileAttributes attrs) => (attrs & FileAttributes.Hidden) != 0;

    /// <summary>
    /// 사용자 설정(showHidden)까지 반영해 목록에서 제외할지 판단.
    /// 목록을 채우는 모든 경로는 이걸 써야 한다.
    /// </summary>
    public static bool ShouldHide(FileAttributes attrs, bool showHidden) =>
        !showHidden && IsHidden(attrs);

    /// <summary>
    /// <see cref="EnumerationOptions.AttributesToSkip"/>에 넣을 값.
    /// 열거자 레벨에서 거를 때 <see cref="ShouldHide"/>와 같은 결과를 낸다.
    /// </summary>
    public static FileAttributes AttributesToSkip(bool showHidden) =>
        showHidden ? FileAttributes.None : FileAttributes.Hidden;
}
