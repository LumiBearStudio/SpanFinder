using System.Text;
using Span.Helpers;

namespace Span.Tests.Helpers;

/// <summary>
/// ZIP 엔트리 이름 디코딩을 시스템 코드페이지(GetACP)와 형식 코드페이지(CurrentCulture) 조합별로
/// 검증한다. 실제 PC 설정과 무관하게 돌도록 두 코드페이지를 직접 넣는다.
///
/// 배경(2026-11-09): 형식 코드페이지만 보던 판정이 "한국어 시스템 + 영어(미국) 형식"에서 1252가 되어
/// 반디집·탐색기가 만든 CP949 이름을 깨뜨렸다. 시스템 쪽만 보게 바꾸면 반대 조합
/// ("영문 Windows + 형식만 한국")이 새로 깨지므로 두 후보를 함께 본다.
/// </summary>
[TestClass]
public class ZipEntryNameEncodingTests
{
    private const string KoreanName = "주문서_한글파일.txt";
    private const string LatinName = "café_Größe.txt";
    private const char Replacement = (char)0xFFFD;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static byte[] Bytes(int codePage, string text) => Encoding.GetEncoding(codePage).GetBytes(text);

    private static string Decode(int systemCodePage, int formatCodePage, byte[] nameBytes)
        => new ZipEntryNameEncoding(systemCodePage, formatCodePage).GetString(nameBytes);

    // ── 둘이 같은 경우 (대부분의 사용자) ──

    [TestMethod]
    public void Cp949Name_KoreanSystemAndFormat_Decodes()
        => Assert.AreEqual(KoreanName, Decode(949, 949, Bytes(949, KoreanName)));

    [TestMethod]
    public void LatinName_WesternSystemAndFormat_Decodes()
        => Assert.AreEqual(LatinName, Decode(1252, 1252, Bytes(1252, LatinName)));

    // ── 한국어 시스템 + 영어(미국) 형식: 이번에 고친 조합 ──

    [TestMethod]
    public void Cp949Name_KoreanSystemEnglishFormat_Decodes()
        => Assert.AreEqual(KoreanName, Decode(949, 1252, Bytes(949, KoreanName)));

    [TestMethod]
    public void LatinName_KoreanSystemEnglishFormat_FallsBackToFormatCodePage()
        => Assert.AreEqual(LatinName, Decode(949, 1252, Bytes(1252, LatinName)));

    // ── 영문 Windows + 형식만 한국: 이전에도 되던 조합, 회귀 방지 ──

    [TestMethod]
    public void Cp949Name_EnglishSystemKoreanFormat_StillDecodes()
        => Assert.AreEqual(KoreanName, Decode(1252, 949, Bytes(949, KoreanName)));

    [TestMethod]
    public void LatinName_EnglishSystemKoreanFormat_FallsBackToSystemCodePage()
        => Assert.AreEqual(LatinName, Decode(1252, 949, Bytes(1252, LatinName)));

    // ── UTF-8과 ASCII는 후보와 무관 ──

    [TestMethod]
    public void Utf8Name_MismatchedCodePages_StaysUtf8()
    {
        Assert.AreEqual(KoreanName, Decode(949, 1252, Encoding.UTF8.GetBytes(KoreanName)));
        Assert.AreEqual(KoreanName, Decode(1252, 949, Encoding.UTF8.GetBytes(KoreanName)));
    }

    [TestMethod]
    public void AsciiName_MismatchedCodePages_Unchanged()
        => Assert.AreEqual("plain_file.txt", Decode(949, 1252, Encoding.ASCII.GetBytes("plain_file.txt")));

    // ── 경계 조합 ──

    [TestMethod]
    public void BothDbcs_SystemCodePageTriedFirst()
        => Assert.AreEqual(KoreanName, Decode(949, 932, Bytes(949, KoreanName)));

    [TestMethod]
    public void BothSingleByte_UsesFormatCodePageAsBefore()
    {
        // 1바이트 코드페이지끼리는 엄격 판정으로 가를 수 없어 이전처럼 형식 쪽만 쓴다.
        var bytes = Bytes(1250, "łódź.txt");
        Assert.AreEqual(Encoding.GetEncoding(1252).GetString(bytes), Decode(1250, 1252, bytes));
    }

    [TestMethod]
    public void Utf8SystemCodePage_UsesFormatCodePage()
        => Assert.AreEqual(KoreanName, Decode(65001, 949, Bytes(949, KoreanName)));   // "Beta: Use Unicode UTF-8"

    [TestMethod]
    public void NoLegacyCodePage_FallsBackToUtf8Lax()
    {
        StringAssert.Contains(Decode(0, 0, Bytes(949, KoreanName)), Replacement.ToString());
        StringAssert.Contains(Decode(65001, 65001, Bytes(949, KoreanName)), Replacement.ToString());
    }

    [TestMethod]
    public void Cp949Name_LongerThanStackBuffer_Decodes()
    {
        // CP949 800바이트 이상: 스택 버퍼(512자)를 넘어 ArrayPool 경로를 탄다.
        var longName = string.Concat(Enumerable.Repeat("한글", 200)) + ".txt";
        Assert.AreEqual(longName, Decode(949, 1252, Bytes(949, longName)));
        Assert.AreEqual(longName, Decode(1252, 949, Bytes(949, longName)));
    }
}
