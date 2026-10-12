using System.Collections.Generic;
using System.Linq;

namespace Span.Helpers;

/// <summary>
/// 경로를 클립보드 텍스트로 만들고, 붙여넣은 경로 텍스트를 해석한다.
///
/// Issue #71로 Ctrl+C(파일 복사)는 경로 텍스트를 넣지 않는다. 경로가 필요하면
/// "경로로 복사"(Ctrl+Shift+C)나 우클릭 "경로 복사"를 쓴다.
/// </summary>
public static class PathTextFormatter
{
    private const string LineSeparator = "\r\n";

    /// <summary>
    /// "경로로 복사"(Ctrl+Shift+C) — Windows 11 탐색기와 같은 형식.
    /// 항목마다 항상 큰따옴표로 감싸고, 여러 개면 CRLF로 구분하며, 마지막 줄바꿈은 없다.
    /// </summary>
    public static string FormatQuoted(IEnumerable<string?>? paths)
        => string.Join(LineSeparator, NonEmpty(paths).Select(p => "\"" + p + "\""));

    /// <summary>
    /// 우클릭 "경로 복사" — 따옴표 없이(예전과 같음). 여러 개면 CRLF로 구분한다.
    /// </summary>
    public static string FormatPlain(IEnumerable<string?>? paths)
        => string.Join(LineSeparator, NonEmpty(paths));

    /// <summary>
    /// 입력 전체가 큰따옴표 한 쌍으로 감싸여 있으면 벗긴다. "경로로 복사" 결과를 주소창에
    /// 그대로 붙여넣는 경우를 위한 것이다. Windows 경로에는 큰따옴표가 들어갈 수 없으므로
    /// 안쪽에 따옴표가 더 있으면(여러 경로 등) 손대지 않는다.
    /// </summary>
    public static string? StripEnclosingQuotes(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var t = input.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"' && t.IndexOf('"', 1) == t.Length - 1)
            return t.Substring(1, t.Length - 2).Trim();
        return input;
    }

    /// <summary>
    /// SPAN 내부 압축 경로(<c>archive://D:\a.zip/dir/f.txt</c>)를 탐색기 형식(<c>D:\a.zip\dir\f.txt</c>)으로
    /// 바꾼다. 주소창 표시와 같은 규칙이고, SPAN 주소창은 이 형식을 다시 압축 경로로 해석한다.
    /// 원격(ftp/sftp) 등 다른 경로는 그대로 둔다.
    /// </summary>
    public static string ToDisplayPath(string path)
        => ArchivePathHelper.IsArchivePath(path)
            ? path.Substring(ArchivePathHelper.Prefix.Length).Replace('/', '\\')
            : path;

    private static IEnumerable<string> NonEmpty(IEnumerable<string?>? paths)
        => (paths ?? Enumerable.Empty<string?>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => ToDisplayPath(p!));
}
