using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class HangulSearchHelperTests
{
    // ── ToChosung ───────────────────────────────────

    [TestMethod]
    [DataRow('가', 'ㄱ')]
    [DataRow('나', 'ㄴ')]
    [DataRow('다', 'ㄷ')]
    [DataRow('라', 'ㄹ')]
    [DataRow('마', 'ㅁ')]
    [DataRow('바', 'ㅂ')]
    [DataRow('사', 'ㅅ')]
    [DataRow('아', 'ㅇ')]
    [DataRow('자', 'ㅈ')]
    [DataRow('차', 'ㅊ')]
    [DataRow('카', 'ㅋ')]
    [DataRow('타', 'ㅌ')]
    [DataRow('파', 'ㅍ')]
    [DataRow('하', 'ㅎ')]
    [DataRow('힣', 'ㅎ')]
    public void ToChosung_ReturnsCorrectInitial(char input, char expected)
    {
        Assert.AreEqual(expected, HangulSearchHelper.ToChosung(input));
    }

    [TestMethod]
    [DataRow('A')]
    [DataRow('z')]
    [DataRow('1')]
    [DataRow(' ')]
    [DataRow('!')]
    public void ToChosung_NonHangul_ReturnsOriginal(char input)
    {
        Assert.AreEqual(input, HangulSearchHelper.ToChosung(input));
    }

    [TestMethod]
    public void ToChosung_DoubleConsonant_ReturnsDouble()
    {
        // '까' (0xAE4C) → 'ㄲ'
        Assert.AreEqual('ㄲ', HangulSearchHelper.ToChosung('까'));
        // '따' → 'ㄸ'
        Assert.AreEqual('ㄸ', HangulSearchHelper.ToChosung('따'));
        // '빠' → 'ㅃ'
        Assert.AreEqual('ㅃ', HangulSearchHelper.ToChosung('빠'));
        // '싸' → 'ㅆ'
        Assert.AreEqual('ㅆ', HangulSearchHelper.ToChosung('싸'));
        // '짜' → 'ㅉ'
        Assert.AreEqual('ㅉ', HangulSearchHelper.ToChosung('짜'));
    }

    // ── ToChosungString ─────────────────────────────

    [TestMethod]
    public void ToChosungString_PureKorean_ConvertsAll()
    {
        Assert.AreEqual("ㅂㅅㅎㄱ", HangulSearchHelper.ToChosungString("복사하기"));
        Assert.AreEqual("ㅎㄱㅁㄷㄱ", HangulSearchHelper.ToChosungString("한글모드기"));
    }

    [TestMethod]
    public void ToChosungString_Mixed_PreservesNonHangul()
    {
        Assert.AreEqual("ㅂㅅ Copy", HangulSearchHelper.ToChosungString("복사 Copy"));
        Assert.AreEqual("Open ㅍㅇ", HangulSearchHelper.ToChosungString("Open 파일"));
    }

    [TestMethod]
    public void ToChosungString_EmptyOrNull_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, HangulSearchHelper.ToChosungString(string.Empty));
        Assert.AreEqual(string.Empty, HangulSearchHelper.ToChosungString(null!));
    }

    // ── IsChosungQuery ──────────────────────────────

    [TestMethod]
    [DataRow("ㄱ", true)]
    [DataRow("ㅂㅅ", true)]
    [DataRow("ㄱㄴㄷㄹㅁㅂㅅㅇㅈㅊㅋㅌㅍㅎ", true)]
    [DataRow("가", false)]                  // 음절
    [DataRow("ㅂㅅ복사", false)]           // 음절 섞임
    [DataRow("abc", false)]
    [DataRow("ㅏ", false)]                  // 모음
    public void IsChosungQuery_DetectsCorrectly(string query, bool expected)
    {
        Assert.AreEqual(expected, HangulSearchHelper.IsChosungQuery(query));
    }

    [TestMethod]
    public void IsChosungQuery_EmptyOrNull_ReturnsFalse()
    {
        Assert.IsFalse(HangulSearchHelper.IsChosungQuery(string.Empty));
        Assert.IsFalse(HangulSearchHelper.IsChosungQuery(null!));
    }

    // ── Match ───────────────────────────────────────

    [TestMethod]
    public void Match_EmptyQuery_AlwaysTrue()
    {
        Assert.IsTrue(HangulSearchHelper.Match("아무거나", string.Empty));
        Assert.IsTrue(HangulSearchHelper.Match("anything", string.Empty));
    }

    [TestMethod]
    public void Match_EmptyText_FalseUnlessQueryAlsoEmpty()
    {
        Assert.IsFalse(HangulSearchHelper.Match(string.Empty, "x"));
        Assert.IsTrue(HangulSearchHelper.Match(string.Empty, string.Empty));
    }

    [TestMethod]
    public void Match_DirectSubstring_CaseInsensitive()
    {
        Assert.IsTrue(HangulSearchHelper.Match("Open File", "open"));
        Assert.IsTrue(HangulSearchHelper.Match("Open File", "FILE"));
        Assert.IsTrue(HangulSearchHelper.Match("Open File", "n F"));
    }

    [TestMethod]
    public void Match_KoreanSubstring_Direct()
    {
        Assert.IsTrue(HangulSearchHelper.Match("복사하기", "복사"));
        Assert.IsTrue(HangulSearchHelper.Match("새 폴더 만들기", "폴더"));
    }

    [TestMethod]
    public void Match_ChosungQuery_MatchesByInitials()
    {
        Assert.IsTrue(HangulSearchHelper.Match("복사하기", "ㅂㅅ"));      // 복사
        Assert.IsTrue(HangulSearchHelper.Match("복사하기", "ㅂㅅㅎㄱ"));  // 복사하기
        Assert.IsTrue(HangulSearchHelper.Match("새 폴더 만들기", "ㅍㄷ"));
        Assert.IsTrue(HangulSearchHelper.Match("이름 바꾸기", "ㅇㄹ"));
    }

    [TestMethod]
    public void Match_ChosungQuery_NoMatch()
    {
        Assert.IsFalse(HangulSearchHelper.Match("복사하기", "ㅋㅌ"));
    }

    [TestMethod]
    public void Match_FuzzyEnglish_AcceptsOrderedSubsequence()
    {
        // "OpenFile"에 'o','f','i','l' 순서대로 등장
        Assert.IsTrue(HangulSearchHelper.Match("OpenFile", "ofil"));
        Assert.IsTrue(HangulSearchHelper.Match("CommandPalette", "cmd"));
    }

    [TestMethod]
    public void Match_FuzzyEnglish_RejectsWrongOrder()
    {
        // 순서가 어긋나면 false
        Assert.IsFalse(HangulSearchHelper.Match("OpenFile", "filo"));
    }

    [TestMethod]
    public void Match_KoreanSyllableInQuery_NotFuzzy()
    {
        // 한글 음절이 직접 포함되어 있지 않으면 false (fuzzy 회피)
        Assert.IsFalse(HangulSearchHelper.Match("OpenFile", "복사"));
    }

    // ── Score ───────────────────────────────────────

    [TestMethod]
    public void Score_Prefix_HighestEnglish()
    {
        Assert.AreEqual(100, HangulSearchHelper.Score("OpenFile", "open"));
    }

    [TestMethod]
    public void Score_Contains_Mid()
    {
        Assert.AreEqual(60, HangulSearchHelper.Score("MyOpenFile", "open"));
    }

    [TestMethod]
    public void Score_ChosungPrefix_90()
    {
        // "복사하기" 초성 → "ㅂㅅㅎㄱ", 쿼리 "ㅂㅅ"는 prefix
        Assert.AreEqual(90, HangulSearchHelper.Score("복사하기", "ㅂㅅ"));
    }

    [TestMethod]
    public void Score_ChosungContains_50()
    {
        // "새 복사" → 초성 "ㅅ ㅂㅅ", 쿼리 "ㅂㅅ"는 prefix가 아니라 contains
        Assert.AreEqual(50, HangulSearchHelper.Score("새 복사", "ㅂㅅ"));
    }

    [TestMethod]
    public void Score_FuzzyOnly_10()
    {
        Assert.AreEqual(10, HangulSearchHelper.Score("CommandPalette", "cdp"));
    }

    [TestMethod]
    public void Score_NoMatch_Zero()
    {
        Assert.AreEqual(0, HangulSearchHelper.Score("OpenFile", "xyz"));
    }

    [TestMethod]
    public void Score_EmptyInputs_Zero()
    {
        Assert.AreEqual(0, HangulSearchHelper.Score(string.Empty, "abc"));
        Assert.AreEqual(0, HangulSearchHelper.Score("text", string.Empty));
    }

    [TestMethod]
    public void Score_PrefixBeatsContains()
    {
        var prefix = HangulSearchHelper.Score("OpenFile", "open");
        var contains = HangulSearchHelper.Score("MyOpenFile", "open");
        Assert.IsTrue(prefix > contains);
    }

    [TestMethod]
    public void Score_DirectPrefixBeatsChosungPrefix()
    {
        // 직접 prefix(100)는 초성 prefix(90)보다 우선
        var direct = HangulSearchHelper.Score("복사하기", "복사");      // direct prefix 100
        var chosung = HangulSearchHelper.Score("복사하기", "ㅂㅅ");     // chosung prefix 90
        Assert.AreEqual(100, direct);
        Assert.AreEqual(90, chosung);
        Assert.IsTrue(direct > chosung);
    }

    [TestMethod]
    public void Score_ChosungPrefixBeatsDirectContains()
    {
        // 초성 prefix(90)는 직접 contains(60)보다 우선
        var contains = HangulSearchHelper.Score("새 복사", "복사");     // direct contains 60
        var chosungPre = HangulSearchHelper.Score("복사하기", "ㅂㅅ"); // chosung prefix 90
        Assert.IsTrue(chosungPre > contains);
    }
}
