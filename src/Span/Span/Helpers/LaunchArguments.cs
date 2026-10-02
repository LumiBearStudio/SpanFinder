using System;
using System.Collections.Generic;

namespace Span.Helpers;

internal enum LaunchKind
{
    /// <summary>열 경로가 없다 — 세션 복원(콜드 스타트) 또는 창만 앞으로(리다이렉트).</summary>
    None,
    /// <summary>점프 목록 '새 창'.</summary>
    NewWindow,
    /// <summary>폴더면 열고, 파일이면 부모 폴더를 열어 선택한다. shell:·::{CLSID}도 여기로 온다.</summary>
    Path,
    /// <summary>--select: 부모 폴더를 열고 그 항목을 선택한다. 폴더 항목이어도 안으로 들어가지 않는다.</summary>
    Select,
}

internal readonly record struct LaunchRequest(LaunchKind Kind, string? Path)
{
    public static LaunchRequest Empty => new(LaunchKind.None, null);
    public static LaunchRequest NewWindowRequest => new(LaunchKind.NewWindow, null);
    public override string ToString() => Path is null ? Kind.ToString() : $"{Kind}:{Path}";
}

/// <summary>
/// SPAN 실행 인자 해석. 경로마다 모양이 다르다:
///   앱 실행 별칭·기본 파일 관리자 레지스트리  "…\WindowsApps\spanfinder.exe" "D:\folder"   (argv0 포함)
///   점프 목록 최근 폴더                       D:\My Folder                                (argv0·따옴표 없음)
///   점프 목록 '새 창'                         --new-window
///   셸 가상 폴더                              "::{CLSID}", shell:RecycleBinFolder
/// 따옴표는 구분자일 뿐 이스케이프는 없다 — Windows 경로에는 '"'가 들어갈 수 없다.
/// CommandLineToArgvW와 .NET의 인자 분리는 "C:\" 를 C:" 로 깨뜨리므로 쓰지 않는다.
///
/// 순수 함수다. 존재 여부 판정은 주입받는다(단위 테스트: Span.Tests LaunchArgumentsTests).
/// </summary>
internal static class LaunchArguments
{
    public const string NewWindowFlag = "--new-window";
    public const string SelectFlag = "--select";

    public static LaunchRequest Parse(string? raw, Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(raw)) return LaunchRequest.Empty;

        var tokens = Tokenize(raw);

        // argv0(별칭 또는 Span.exe 경로)는 버린다. 위치로 판정한다 — 경로 패턴(IsSystemPackagePath)만으로는
        // 개발 등록 패키지(bin\...\AppX\Span.exe)에서 걸러지지 않는다.
        int start = tokens.Count > 0 && IsSpanExecutable(tokens[0].Text) ? 1 : 0;
        if (start >= tokens.Count) return LaunchRequest.Empty;

        for (int i = start; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i].Text, NewWindowFlag, StringComparison.OrdinalIgnoreCase))
                return LaunchRequest.NewWindowRequest;

            if (string.Equals(tokens[i].Text, SelectFlag, StringComparison.OrdinalIgnoreCase))
            {
                // 선택은 파일 시스템 항목에만 의미가 있다. 가상 폴더·shell: 경로는 콜드 스타트와 리다이렉트의
                // 처리가 갈리므로(위임·휴지통 뷰 vs 무시) 아예 받지 않는다.
                var target = i + 1 < tokens.Count ? tokens[i + 1].Text : "";
                return target.Length > 0 && !IsShellNamespacePath(target)
                    ? new LaunchRequest(LaunchKind.Select, target)
                    : LaunchRequest.Empty;
            }
        }

        // 마지막 인수부터 역순 (이전 App.ExtractFolderArgument와 같은 우선순위)
        for (int i = tokens.Count - 1; i >= start; i--)
        {
            var part = tokens[i].Text;
            if (part.Length == 0 || IsSystemPackagePath(part)) continue;

            if (directoryExists(part) || fileExists(part)
                || IsRecycleBin(part) || IsVirtualFolder(part) || IsThisPC(part)
                || part.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return new LaunchRequest(LaunchKind.Path, part);
        }

        // 따옴표 없이 공백이 든 경로(점프 목록 "D:\My Folder"): argv0 뒤 나머지를 통째로 본다
        var rest = Clean(raw.Substring(tokens[start].Start));
        if (rest.Length > 0 && !IsSystemPackagePath(rest) && (directoryExists(rest) || fileExists(rest)))
            return new LaunchRequest(LaunchKind.Path, rest);

        // 아무것도 아니면 None. 예전에는 원본 문자열을 그대로 돌려줘서, 인자 없이 별칭을 실행하면
        // (Win+R 'spanfinder') argv0인 WindowsApps\spanfinder.exe가 경로로 쓰여 WindowsApps 폴더가 열렸다.
        return LaunchRequest.Empty;
    }

    public static bool IsNewWindow(string? arg) =>
        string.Equals(arg?.Trim().Trim('"'), NewWindowFlag, StringComparison.OrdinalIgnoreCase);

    /// <summary>휴지통 관련 shell 인자인지.</summary>
    public static bool IsRecycleBin(string? arg) =>
        !string.IsNullOrEmpty(arg)
        && (arg.Contains("RecycleBinFolder", StringComparison.OrdinalIgnoreCase)
            || arg.Contains("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase));

    /// <summary>This PC(내 PC) CLSID인지. SPAN 홈으로 매핑한다.</summary>
    public static bool IsThisPC(string? arg) =>
        !string.IsNullOrEmpty(arg)
        && arg.Contains("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CLSID 가상 폴더 경로인지(This PC 제외). 제어판·네트워크·프린터 등 SPAN이 탐색할 수 없어
    /// explorer.exe에 위임하는 폴더.
    /// </summary>
    public static bool IsVirtualFolder(string? arg) =>
        !string.IsNullOrEmpty(arg)
        && arg.StartsWith("::{", StringComparison.OrdinalIgnoreCase)
        && !IsThisPC(arg);

    /// <summary>
    /// MSIX/WindowsApps 패키지 경로. 기본 파일 관리자로 등록했을 때 탐색기 아이콘을 누르면
    /// 패키지 폴더가 인자로 오는 경우가 있어 거른다.
    /// </summary>
    public static bool IsSystemPackagePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Contains(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase)) return true;
        return path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            && path.Contains("Program Files", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShellNamespacePath(string path) =>
        path.StartsWith("::", StringComparison.Ordinal)
        || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
        || IsRecycleBin(path) || IsThisPC(path);

    internal readonly record struct Token(string Text, int Start);

    /// <summary>
    /// 따옴표로 감싼 구간은 한 토큰, 아니면 공백까지가 한 토큰. Start는 원문에서 토큰 내용이 시작하는 위치.
    /// </summary>
    internal static List<Token> Tokenize(string raw)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '"')
            {
                int end = raw.IndexOf('"', i + 1);
                if (end < 0) end = raw.Length;
                tokens.Add(new Token(Clean(raw.Substring(i + 1, end - i - 1)), i + 1));
                i = end + 1;
            }
            else if (raw[i] != ' ')
            {
                int end = raw.IndexOf(' ', i);
                if (end < 0) end = raw.Length;
                tokens.Add(new Token(Clean(raw.Substring(i, end - i)), i));
                i = end;
            }
            else
            {
                i++;
            }
        }
        return tokens;
    }

    // 셸이 CLSID 끝에 \0을 붙여 넘기는 경우가 있다
    private static string Clean(string s) => s.Trim().Trim('"').Trim('\0').Trim();

    private static bool IsSpanExecutable(string token)
    {
        int slash = Math.Max(token.LastIndexOf('\\'), token.LastIndexOf('/'));
        var name = slash >= 0 ? token.Substring(slash + 1) : token;
        // Span.dll: .NET apphost 앱의 Environment.CommandLine은 argv0으로 관리 어셈블리 경로를 돌려준다
        return name.Equals("spanfinder.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Span.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Span.dll", StringComparison.OrdinalIgnoreCase);
    }
}
