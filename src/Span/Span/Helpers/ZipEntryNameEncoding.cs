using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Span.Helpers;

/// <summary>
/// ZIP 엔트리 이름을 항목별로 자동 판별해 디코딩하는 인코딩. 읽기 경로 전용.
///
/// 배경: ZIP은 파일명 인코딩을 EFS 비트(일반 목적 플래그 11번)로만 알린다. 그런데
/// 한국에서 실제로 쓰이는 도구들은 이 비트를 켜지 않는다 — 반디집과 윈도우 탐색기
/// 기본 압축 모두 CP949 바이트를 EFS 없이 기록한다(실측 확인).
///
/// .NET Framework 시절에는 인코딩 미지정 시 ANSI 코드페이지(한국어 Windows = CP949)로
/// 디코딩되어 문제가 없었으나, .NET Core 이후 <see cref="Encoding.Default"/>가 UTF-8로
/// 바뀌면서 이 이름들이 UTF-8로 오디코딩된다. 결과에 U+FFFD가 섞이므로 원본 바이트를
/// 되돌릴 수 없다(비가역 손실).
///
/// 반대로 고정 코드페이지(CP949)를 지정하면 UTF-8로 기록된 정상 아카이브가 깨진다.
/// 그래서 이름 바이트가 유효한 UTF-8인지 항목마다 검사해 인코딩을 고른다.
///
/// 실측 결과 (주문서_한글파일.txt):
///   아카이브 형태          인코딩 미지정   고정 CP949   이 클래스
///   CP949 + EFS 없음       깨짐            정상         정상
///   UTF-8 + EFS 있음       정상            깨짐         정상
///   UTF-8 + EFS 없음       정상            깨짐         정상
///
/// 판별은 예외가 아니라 <see cref="Utf8.ToUtf16"/>로 한다. 예외 기반은 70,000 엔트리
/// 아카이브에서 616ms가 걸리지만 이 방식은 2ms다(실측).
///
/// UTF-8이 아닌 이름의 코드페이지 (2026-11-09):
///   후보는 두 개다 — 시스템 코드페이지(GetACP, "유니코드를 지원하지 않는 프로그램용 언어")와
///   사용자 "형식" 설정의 ANSI 코드페이지(.NET의 CultureInfo.CurrentCulture는 형식 설정이다).
///   처음에는 형식 쪽만 써서, 한국어 시스템에 영어(미국) 형식이면 1252가 되어 CP949 이름이
///   깨졌다. 시스템 쪽만 쓰면 반대 조합(영문 Windows 설치 때 지역만 한국으로 고른 경우:
///   시스템 1252, 형식 ko-KR)이 새로 깨진다.
///   그래서 둘이 다르고 한쪽이 한중일 2바이트 코드페이지(932/936/949/950)면, 그 코드페이지로
///   먼저 엄격하게 풀어 보고 잘못된 바이트가 하나라도 있으면 다른 쪽을 쓴다. 둘 다 2바이트면
///   시스템 쪽이 먼저다. 둘 다 1바이트 코드페이지면 이전처럼 형식 쪽만 쓴다 — 1바이트
///   코드페이지는 거의 모든 바이트를 받아들여 엄격 판정으로 가를 수 없다.
///   둘이 같으면(대부분의 사용자) 이전과 완전히 같게 동작한다.
/// </summary>
internal sealed class ZipEntryNameEncoding : Encoding
{
    /// <summary>
    /// 공용 인스턴스. ZIP <b>읽기</b> 경로에서만 쓴다.
    /// 쓰기(압축 생성)는 .NET 기본 동작(UTF-8 + EFS 비트)이 상호운용에 맞으므로 건드리지 않는다.
    /// </summary>
    internal static readonly ZipEntryNameEncoding Instance = new();

    /// <summary>잘못된 바이트를 U+FFFD로 치환하는 UTF-8. 판별을 통과한 이름에만 쓰인다.</summary>
    private static readonly UTF8Encoding Utf8Lax = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>엄격 판정에서 잘못된 바이트 자리에 들어가는 문자(U+FFFD).</summary>
    private const char Replacement = (char)0xFFFD;

    /// <summary>UTF-8이 아닌 이름에 먼저 쓸 코드페이지. 쓸 수 있는 게 없으면 null(UTF-8만 쓴다).</summary>
    private readonly Encoding? _primary;

    /// <summary>이름이 <see cref="_primary"/>로 깨끗이 풀리지 않을 때 쓸 코드페이지. 후보가 하나면 null.</summary>
    private readonly Encoding? _secondary;

    /// <summary>
    /// <see cref="_primary"/> 판정용. 잘못된 바이트를 U+FFFD로 바꿔 셀 수 있게 한다 — 표시용
    /// 인코딩의 기본 치환 문자는 '?'라 이름에 든 물음표와 구분되지 않는다.
    /// </summary>
    private readonly Encoding? _primaryStrict;

    private ZipEntryNameEncoding() : this(SystemCodePage(), FormatCodePage()) { }

    /// <summary>테스트용: 시스템 코드페이지와 형식 코드페이지를 직접 준다. 0은 "없음"이다.</summary>
    internal ZipEntryNameEncoding(int systemCodePage, int formatCodePage)
    {
        var system = TryGetAnsi(systemCodePage);
        var format = TryGetAnsi(formatCodePage);

        // 후보가 하나뿐이거나 둘이 같으면 이전 동작과 같다. 형식 쪽이 먼저인 것도 이전과 같다
        // (시스템 ANSI가 UTF-8인 "Beta: Use Unicode UTF-8" 환경에서는 시스템 쪽이 빠진다).
        _primary = format ?? system;
        if (system is null || format is null || system.CodePage == format.CodePage)
            return;

        bool systemDbcs = IsDbcs(system.CodePage);
        if (!systemDbcs && !IsDbcs(format.CodePage))
            return;

        try
        {
            var primary = systemDbcs ? system : format;
            _primaryStrict = GetEncoding(primary.CodePage, EncoderFallback.ReplacementFallback,
                new DecoderReplacementFallback(Replacement.ToString()));
            _primary = primary;
            _secondary = systemDbcs ? format : system;
            DebugLogger.Log($"[Zip] non-UTF-8 entry names: system {system.CodePage}, format {format.CodePage} -> try {_primary.CodePage}, then {_secondary.CodePage}");
        }
        catch (Exception ex)
        {
            // 판정용 인코딩을 못 만들면 이전 동작(형식 쪽 하나)으로 남는다.
            DebugLogger.Log($"[Zip] strict decoder unavailable, using format code page only: {ex.GetType().Name}");
        }
    }

    /// <summary>시스템 ANSI 코드페이지(GetACP). 알 수 없으면 0.</summary>
    private static int SystemCodePage()
    {
        try
        {
            // .NET Core는 기본적으로 레거시 코드페이지를 싣지 않는다. 등록은 멱등이다.
            RegisterProvider(CodePagesEncodingProvider.Instance);

            // 공급자를 등록하면 GetEncoding(0)은 시스템 ANSI 코드페이지(GetACP)를 돌려준다
            // (TextEncodingDetector와 같은 방식). ACP가 UTF-8이면 65001이 오고 후보에서 빠진다.
            return GetEncoding(0).CodePage;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Zip] system code page unavailable: {ex.GetType().Name}");
            return 0;
        }
    }

    /// <summary>사용자 "형식" 설정(CurrentCulture)의 ANSI 코드페이지. 알 수 없으면 0.</summary>
    private static int FormatCodePage()
    {
        try
        {
            return CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Zip] format code page unavailable: {ex.GetType().Name}");
            return 0;
        }
    }

    /// <summary>쓸 수 있는 레거시 코드페이지면 그 인코딩을, 아니면 null(0, UTF-8, 실패).</summary>
    private static Encoding? TryGetAnsi(int codePage)
    {
        // UTF-8은 폴백으로 의미가 없다 — UTF-8 경로가 이미 처리한다.
        if (codePage <= 0 || codePage == 65001)
            return null;
        try
        {
            RegisterProvider(CodePagesEncodingProvider.Instance);
            return GetEncoding(codePage);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Zip] code page {codePage} unavailable: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>한중일 2바이트 코드페이지(Shift-JIS, GBK, CP949, Big5).</summary>
    private static bool IsDbcs(int codePage) => codePage is 932 or 936 or 949 or 950;

    /// <summary>
    /// 이름 바이트가 유효한 UTF-8이면 UTF-8을, 아니면 레거시 코드페이지를 고른다.
    /// 후보가 둘이면 첫 번째로 깨끗이 풀릴 때만 첫 번째를 쓴다.
    /// </summary>
    private Encoding Pick(byte[] bytes, int index, int count)
    {
        if (_primary is null || count == 0)
            return Utf8Lax;

        // UTF-8 -> UTF-16 변환에서 문자 수는 바이트 수를 넘지 않는다. 엄격 판정은 디코더가
        // 말하는 최대치를 따른다.
        int needed = _primaryStrict is null ? count : Math.Max(count, _primaryStrict.GetMaxCharCount(count));
        char[]? rented = null;
        try
        {
            Span<char> scratch = needed <= 512
                ? stackalloc char[512]
                : (rented = ArrayPool<char>.Shared.Rent(needed));

            var name = bytes.AsSpan(index, count);
            var status = Utf8.ToUtf16(
                name, scratch, out _, out _,
                replaceInvalidSequences: false, isFinalBlock: true);

            if (status == OperationStatus.Done)
                return Utf8Lax;

            if (_secondary is null || _primaryStrict is null)
                return _primary;

            int written = _primaryStrict.GetChars(name, scratch);
            return scratch[..written].Contains(Replacement) ? _secondary : _primary;
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    public override int GetCharCount(byte[] bytes, int index, int count)
        => Pick(bytes, index, count).GetCharCount(bytes, index, count);

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        => Pick(bytes, byteIndex, byteCount).GetChars(bytes, byteIndex, byteCount, chars, charIndex);

    public override int GetMaxCharCount(int byteCount)
        => Math.Max(Utf8Lax.GetMaxCharCount(byteCount),
            Math.Max(_primary?.GetMaxCharCount(byteCount) ?? 0, _secondary?.GetMaxCharCount(byteCount) ?? 0));

    // 아래 인코딩(문자 -> 바이트) 경로는 읽기 전용 사용이라 호출되지 않지만,
    // Encoding이 추상 멤버로 요구하므로 UTF-8로 위임한다.
    public override int GetByteCount(char[] chars, int index, int count)
        => Utf8Lax.GetByteCount(chars, index, count);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => Utf8Lax.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    public override int GetMaxByteCount(int charCount)
        => Utf8Lax.GetMaxByteCount(charCount);
}
