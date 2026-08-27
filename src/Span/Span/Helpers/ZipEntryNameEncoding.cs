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

    /// <summary>UTF-8이 아닌 이름에 쓸 OS ANSI 코드페이지. 사용할 수 없으면 null.</summary>
    private readonly Encoding? _ansi = ResolveAnsi();

    private ZipEntryNameEncoding() { }

    private static Encoding? ResolveAnsi()
    {
        try
        {
            // .NET Core는 기본적으로 레거시 코드페이지를 싣지 않는다. 등록은 멱등이다.
            RegisterProvider(CodePagesEncodingProvider.Instance);

            int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

            // ANSI가 UTF-8인 환경("Beta: Use Unicode UTF-8 for worldwide language support")에서는
            // 폴백이 의미가 없다 — UTF-8 경로가 이미 처리한다. 이 경우 기존 동작을 유지한다.
            if (codePage > 0 && codePage != 65001)
                return GetEncoding(codePage);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Zip] ANSI codepage unavailable, falling back to UTF-8 only: {ex.GetType().Name}");
        }
        return null;
    }

    /// <summary>이름 바이트가 유효한 UTF-8이면 UTF-8을, 아니면 ANSI 코드페이지를 고른다.</summary>
    private Encoding Pick(byte[] bytes, int index, int count)
    {
        if (_ansi is null || count == 0)
            return Utf8Lax;

        // UTF-8 -> UTF-16 변환에서 문자 수는 바이트 수를 넘지 않으므로 count 크기면 항상 충분하다.
        char[]? rented = null;
        try
        {
            Span<char> scratch = count <= 256
                ? stackalloc char[256]
                : (rented = ArrayPool<char>.Shared.Rent(count));

            var status = Utf8.ToUtf16(
                bytes.AsSpan(index, count), scratch, out _, out _,
                replaceInvalidSequences: false, isFinalBlock: true);

            return status == OperationStatus.Done ? Utf8Lax : _ansi;
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
        => Math.Max(Utf8Lax.GetMaxCharCount(byteCount), _ansi?.GetMaxCharCount(byteCount) ?? 0);

    // 아래 인코딩(문자 -> 바이트) 경로는 읽기 전용 사용이라 호출되지 않지만,
    // Encoding이 추상 멤버로 요구하므로 UTF-8로 위임한다.
    public override int GetByteCount(char[] chars, int index, int count)
        => Utf8Lax.GetByteCount(chars, index, count);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => Utf8Lax.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    public override int GetMaxByteCount(int charCount)
        => Utf8Lax.GetMaxByteCount(charCount);
}
