using System;
using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace Span.Helpers;

/// <summary>
/// BOM 없는 텍스트 파일의 인코딩을 고른다. UTF-8이면 UTF-8, 아니면 OS ANSI 코드페이지
/// (한국어 Windows = CP949, 중국어 간체 = 936/GBK, 일본어 = 932/Shift-JIS). 읽기 전용.
///
/// 배경(Issue #69): 미리보기 로더가 BOM이 없으면 UTF-8로만 읽어 CP949 자막(.srt/.smi)이나
/// ANSI로 저장한 텍스트가 '�'로 가득 찼다. .NET Core 이후 Encoding.Default가 UTF-8로 바뀌어
/// .NET Framework 시절의 암묵적 ANSI 폴백이 사라진 것이다 — ZipEntryNameEncoding과 같은 문제.
///
/// 판정은 세 단계다. 틀리면 출하본과 같은 UTF-8(lax)로 떨어지도록 짰다 — UTF-8 lax는 잘못된
/// 바이트마다 '�' 하나를 넣고 ASCII는 전부 보존한다.
///   1) UTF-8로 풀어 잘못된 시퀀스와 유효한 멀티바이트 문자를 센다. 잘못된 게 없거나, 유효한 게
///      잘못된 것의 두 배 이상이면 UTF-8. UTF-8은 구조가 엄격해(선두 바이트 + 정확한 개수의
///      10xxxxxx) CP949·GBK 바이트열이 우연히 이 조건을 채우기 어렵다. 파일명용 규칙("하나라도
///      잘못되면 ANSI", ZipEntryNameEncoding)을 본문에 쓰면 깨진 바이트 몇 개 섞인 UTF-8이
///      통째로 ANSI로 해석돼 전부 깨진다.
///   2) 끝에서 잘린 문자는 세지 않는다 — 파일 전체를 읽었어도. 쓰는 중인 로그는 끝에서 글자가
///      잘려 있기 마련이고, 그걸 오류로 세면 비ASCII가 적은 로그가 통째로 ANSI가 된다.
///   3) 1)을 통과 못 하면 ANSI로도 풀어 본다. ANSI로도 깨끗이 풀리지 않으면(잘못된 문자가 비ASCII
///      바이트의 5% 이상) UTF-8 lax를 쓴다. DBCS(949/936/932/950) 디코더는 잘못된 바이트 쌍에서
///      뒤따르는 ASCII까지 먹는다 — CJK 로캘에서 Latin-1 CSV를 ANSI로 읽으면 쉼표·줄바꿈이
///      사라져 열과 줄이 합쳐진다.
///
/// 코드페이지는 Encoding.GetEncoding(0) = 시스템 로캘("유니코드를 지원하지 않는 프로그램용
/// 언어")에서 가져온다. 레거시 앱은 이 코드페이지로 파일을 저장했다. CultureInfo.CurrentCulture는
/// 사용자의 "형식" 설정이라, 한국어 시스템에 영어(미국) 형식을 쓰면 1252가 돼 CP949 파일을 못
/// 읽는다(ZipEntryNameEncoding이 이 문제를 겪어, 지금은 시스템과 형식 코드페이지를 둘 다 본다).
/// </summary>
internal static class TextEncodingDetector
{
    /// <summary>잘못된 바이트를 U+FFFD로 치환하는 UTF-8. 판정이 UTF-8일 때 쓴다.</summary>
    private static readonly UTF8Encoding Utf8Lax = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>UTF-8이 아닐 때 쓸 OS ANSI 코드페이지. 쓸 수 없으면 null(UTF-8만 쓴다).</summary>
    private static readonly Lazy<Encoding?> Ansi = new(ResolveAnsi);

    /// <summary>
    /// ANSI 검증용. 잘못된 바이트를 U+FFFD로 바꿔 셀 수 있게 한다 — 표시용 인코딩의 기본 치환
    /// 문자는 '?'라 본문의 물음표와 구분되지 않는다.
    /// </summary>
    private static readonly Lazy<Encoding?> AnsiStrict = new(() => Ansi.Value is { } a ? MakeStrict(a.CodePage) : null);

    /// <summary>
    /// BOM이 없을 때 쓸 인코딩을 고른다. head는 파일 앞부분, isWholeFile은 head가 파일 전체인지
    /// (ANSI 검증에서 마지막 DBCS 문자를 마무리할지 정한다).
    /// </summary>
    public static Encoding DetectNoBom(ReadOnlySpan<byte> head, bool isWholeFile)
        => Detect(head, isWholeFile, Ansi.Value, AnsiStrict.Value);

    /// <summary>테스트용: 시스템 코드페이지 대신 주어진 ANSI 코드페이지로 판정한다.</summary>
    internal static Encoding DetectNoBom(ReadOnlySpan<byte> head, bool isWholeFile, Encoding? ansi)
        => Detect(head, isWholeFile, ansi, ansi is null ? null : MakeStrict(ansi.CodePage));

    private static Encoding Detect(ReadOnlySpan<byte> head, bool isWholeFile, Encoding? ansi, Encoding? ansiStrict)
    {
        if (ansi is null || ansiStrict is null) return Utf8Lax;

        int nonAscii = 0;
        foreach (byte b in head)
            if (b >= 0x80) nonAscii++;
        if (nonAscii == 0) return Utf8Lax;   // 순수 ASCII는 어느 쪽으로 읽어도 같다

        // 1) UTF-8로서의 근거
        int invalid = 0, validMultiByte = 0;
        Span<char> scratch = stackalloc char[2048];
        var src = head;
        while (!src.IsEmpty)
        {
            // 2) isFinalBlock은 항상 false — 끝에서 잘린 문자는 NeedMoreData가 되어 세지 않는다.
            var status = Utf8.ToUtf16(src, scratch, out int read, out int written,
                replaceInvalidSequences: false, isFinalBlock: false);

            foreach (char c in scratch[..written])
                if (c >= 0x80 && !char.IsLowSurrogate(c)) validMultiByte++;   // 서로게이트 쌍은 한 글자로

            if (status is OperationStatus.Done or OperationStatus.NeedMoreData)
                break;

            if (status == OperationStatus.DestinationTooSmall)
            {
                if (read == 0) break;
                src = src[read..];
                continue;
            }

            // InvalidData: read는 잘못된 시퀀스 직전까지다. 그 바이트 하나를 건너뛰고 계속 센다.
            invalid++;
            src = src[(read + 1)..];
        }
        if (invalid == 0 || validMultiByte >= invalid * 2)
            return Utf8Lax;

        // 3) ANSI로도 깨끗이 풀리는지
        int ansiBad = CountReplacements(ansiStrict, head, flush: isWholeFile);
        return ansiBad * 20 >= nonAscii ? Utf8Lax : ansi;
    }

    /// <summary>
    /// head를 strict로 풀어 U+FFFD 개수를 센다. 2048자 청크로 돌려 큰 배열을 잡지 않는다
    /// (64KB head를 한 번에 풀면 LOH 할당이 된다).
    /// </summary>
    private static int CountReplacements(Encoding strict, ReadOnlySpan<byte> bytes, bool flush)
    {
        var decoder = strict.GetDecoder();
        Span<char> chars = stackalloc char[2048];
        int bad = 0;
        bool completed;
        do
        {
            decoder.Convert(bytes, chars, flush, out int bytesUsed, out int charsUsed, out completed);
            foreach (char c in chars[..charsUsed])
                if (c == '�') bad++;
            if (bytesUsed == 0 && charsUsed == 0) break;   // 진행이 없으면 멈춘다(방어)
            bytes = bytes[bytesUsed..];
        } while (!completed);
        return bad;
    }

    private static Encoding MakeStrict(int codePage)
        => Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("�"));

    private static Encoding? ResolveAnsi()
    {
        try
        {
            // .NET Core는 레거시 코드페이지를 기본으로 싣지 않는다. 등록은 멱등이다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 공급자를 등록하면 GetEncoding(0)은 시스템 ANSI 코드페이지(GetACP)를 돌려준다.
            var encoding = Encoding.GetEncoding(0);

            // ANSI가 UTF-8인 환경("Beta: Use Unicode UTF-8 for worldwide language support")에서는
            // 폴백이 의미가 없다 — UTF-8 경로가 이미 처리한다.
            if (encoding.CodePage != 65001)
                return encoding;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[TextEncoding] ANSI codepage unavailable, UTF-8 only: {ex.GetType().Name}");
        }
        return null;
    }
}
