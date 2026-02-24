using Span.Helpers;
using Span.Models;

namespace Span.Tests.Helpers;

[TestClass]
public class SearchQueryParserTests
{
    // -------------------------------------------------------
    // 1. Empty / null / whitespace input
    // -------------------------------------------------------

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\n")]
    public void Parse_EmptyOrWhitespaceInput_ReturnsIsEmptyQuery(string? input)
    {
        var query = SearchQueryParser.Parse(input!);

        Assert.IsTrue(query.IsEmpty);
        Assert.IsNull(query.NameFilter);
        Assert.IsNull(query.KindFilter);
        Assert.IsNull(query.SizeFilter);
        Assert.IsNull(query.DateFilter);
        Assert.IsNull(query.ExtensionFilter);
    }

    // -------------------------------------------------------
    // 2. Plain text -> NameFilter
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_PlainText_SetsNameFilter()
    {
        var query = SearchQueryParser.Parse("hello");

        Assert.AreEqual("hello", query.NameFilter);
        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void Parse_MultipleNameTokens_JoinedWithSpace()
    {
        var query = SearchQueryParser.Parse("my file");

        Assert.AreEqual("my file", query.NameFilter);
    }

    [TestMethod]
    public void Parse_QuotedString_TreatedAsSingleToken()
    {
        var query = SearchQueryParser.Parse("\"hello world\"");

        Assert.AreEqual("hello world", query.NameFilter);
    }

    [TestMethod]
    public void Parse_SingleQuotedString_TreatedAsSingleToken()
    {
        var query = SearchQueryParser.Parse("'hello world'");

        Assert.AreEqual("hello world", query.NameFilter);
    }

    // -------------------------------------------------------
    // 3. kind: filter with aliases
    // -------------------------------------------------------

    [TestMethod]
    [DataRow("kind:image", FileKind.Image)]
    [DataRow("kind:photo", FileKind.Image)]
    [DataRow("kind:pic", FileKind.Image)]
    [DataRow("kind:img", FileKind.Image)]
    [DataRow("kind:picture", FileKind.Image)]
    [DataRow("kind:photos", FileKind.Image)]
    public void Parse_KindImage_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);

        Assert.AreEqual(expected, query.KindFilter);
        Assert.IsNull(query.NameFilter);
    }

    [TestMethod]
    [DataRow("kind:video", FileKind.Video)]
    [DataRow("kind:movie", FileKind.Video)]
    [DataRow("kind:film", FileKind.Video)]
    public void Parse_KindVideo_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:audio", FileKind.Audio)]
    [DataRow("kind:music", FileKind.Audio)]
    [DataRow("kind:sound", FileKind.Audio)]
    [DataRow("kind:song", FileKind.Audio)]
    public void Parse_KindAudio_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:document", FileKind.Document)]
    [DataRow("kind:doc", FileKind.Document)]
    [DataRow("kind:text", FileKind.Document)]
    public void Parse_KindDocument_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:archive", FileKind.Archive)]
    [DataRow("kind:zip", FileKind.Archive)]
    [DataRow("kind:compressed", FileKind.Archive)]
    public void Parse_KindArchive_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:code", FileKind.Code)]
    [DataRow("kind:source", FileKind.Code)]
    [DataRow("kind:script", FileKind.Code)]
    public void Parse_KindCode_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:exe", FileKind.Executable)]
    [DataRow("kind:executable", FileKind.Executable)]
    [DataRow("kind:app", FileKind.Executable)]
    public void Parse_KindExecutable_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    [DataRow("kind:font", FileKind.Font)]
    [DataRow("kind:fonts", FileKind.Font)]
    public void Parse_KindFont_AllAliases(string input, FileKind expected)
    {
        var query = SearchQueryParser.Parse(input);
        Assert.AreEqual(expected, query.KindFilter);
    }

    [TestMethod]
    public void Parse_KindUnknown_TreatedAsNameToken()
    {
        var query = SearchQueryParser.Parse("kind:unknown");

        Assert.IsNull(query.KindFilter);
        Assert.AreEqual("kind:unknown", query.NameFilter);
    }

    [TestMethod]
    public void Parse_KindCaseInsensitive_Works()
    {
        var query = SearchQueryParser.Parse("Kind:IMAGE");

        Assert.AreEqual(FileKind.Image, query.KindFilter);
    }

    // -------------------------------------------------------
    // 4. size: filter - named presets
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_SizeEmpty_ReturnsEqualsZero()
    {
        var query = SearchQueryParser.Parse("size:empty");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.Equals, query.SizeFilter!.Value.Op);
        Assert.AreEqual(0L, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeTiny_ReturnsLessThan16KB()
    {
        var query = SearchQueryParser.Parse("size:tiny");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.LessThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(16L * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeSmall_ReturnsLessThan1MB()
    {
        var query = SearchQueryParser.Parse("size:small");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.LessThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeLarge_ReturnsGreaterThan128MB()
    {
        var query = SearchQueryParser.Parse("size:large");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(128L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    [DataRow("size:huge")]
    [DataRow("size:gigantic")]
    public void Parse_SizeHuge_ReturnsGreaterThan1GB(string input)
    {
        var query = SearchQueryParser.Parse(input);

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeMedium_ReturnsGreaterOrEqual1MB()
    {
        var query = SearchQueryParser.Parse("size:medium");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    // -------------------------------------------------------
    // 5. size: filter - numeric with operators and units
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_SizeGreaterThan1MB_Parsed()
    {
        var query = SearchQueryParser.Parse("size:>1MB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeLessThan100KB_Parsed()
    {
        var query = SearchQueryParser.Parse("size:<100KB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.LessThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(100L * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeGreaterOrEqual500B_Parsed()
    {
        var query = SearchQueryParser.Parse("size:>=500B");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.SizeFilter!.Value.Op);
        Assert.AreEqual(500L, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeLessOrEqual2GB_Parsed()
    {
        var query = SearchQueryParser.Parse("size:<=2GB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.LessOrEqual, query.SizeFilter!.Value.Op);
        Assert.AreEqual(2L * 1024 * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeEquals10MB_Parsed()
    {
        var query = SearchQueryParser.Parse("size:=10MB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.Equals, query.SizeFilter!.Value.Op);
        Assert.AreEqual(10L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeNoOperator_DefaultsToGreaterOrEqual()
    {
        var query = SearchQueryParser.Parse("size:1GB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeUnitCaseInsensitive_Works()
    {
        var query = SearchQueryParser.Parse("size:>1mb");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizePrefixCaseInsensitive_Works()
    {
        var query = SearchQueryParser.Parse("SIZE:>1MB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeNoUnit_TreatedAsBytes()
    {
        var query = SearchQueryParser.Parse("size:>1024");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1024L, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_SizeTB_Parsed()
    {
        var query = SearchQueryParser.Parse("size:>1TB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024 * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    // -------------------------------------------------------
    // 6. date: filter - named presets
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_DateToday_ReturnsGreaterOrEqualToday()
    {
        var query = SearchQueryParser.Parse("date:today");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(DateTime.Now.Date, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateYesterday_ReturnsGreaterOrEqualYesterday()
    {
        var query = SearchQueryParser.Parse("date:yesterday");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(DateTime.Now.Date.AddDays(-1), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateThisWeek_ReturnsGreaterOrEqualStartOfWeek()
    {
        var query = SearchQueryParser.Parse("date:thisweek");
        var today = DateTime.Now.Date;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var expectedDate = today.AddDays(-daysSinceMonday);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateThisMonth_ReturnsGreaterOrEqualFirstOfMonth()
    {
        var query = SearchQueryParser.Parse("date:thismonth");
        var today = DateTime.Now.Date;
        var expectedDate = new DateTime(today.Year, today.Month, 1);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateThisYear_ReturnsGreaterOrEqualJanFirst()
    {
        var query = SearchQueryParser.Parse("date:thisyear");
        var today = DateTime.Now.Date;
        var expectedDate = new DateTime(today.Year, 1, 1);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateLastWeek_Parsed()
    {
        var query = SearchQueryParser.Parse("date:lastweek");
        var today = DateTime.Now.Date;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var expectedDate = today.AddDays(-daysSinceMonday - 7);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateLastMonth_Parsed()
    {
        var query = SearchQueryParser.Parse("date:lastmonth");
        var today = DateTime.Now.Date;
        var lastMonth = today.AddMonths(-1);
        var expectedDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateLastYear_Parsed()
    {
        var query = SearchQueryParser.Parse("date:lastyear");
        var today = DateTime.Now.Date;
        var expectedDate = new DateTime(today.Year - 1, 1, 1);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(expectedDate, query.DateFilter!.Value.Date);
    }

    // -------------------------------------------------------
    // 7. date: filter - comparison operators
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_DateGreaterThan_Parsed()
    {
        var query = SearchQueryParser.Parse("date:>2024-01-01");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2024, 1, 1), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateLessThan_Parsed()
    {
        var query = SearchQueryParser.Parse("date:<2024-12-31");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.LessThan, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2024, 12, 31), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateGreaterOrEqual_Parsed()
    {
        var query = SearchQueryParser.Parse("date:>=2024-06-15");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2024, 6, 15), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateLessOrEqual_Parsed()
    {
        var query = SearchQueryParser.Parse("date:<=2023-03-20");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.LessOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2023, 3, 20), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateEquals_Parsed()
    {
        var query = SearchQueryParser.Parse("date:=2024-07-04");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.Equals, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2024, 7, 4), query.DateFilter!.Value.Date);
    }

    [TestMethod]
    public void Parse_DateNoOperator_DefaultsToGreaterOrEqual()
    {
        var query = SearchQueryParser.Parse("date:2024-01-01");

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(new DateTime(2024, 1, 1), query.DateFilter!.Value.Date);
    }

    // -------------------------------------------------------
    // 8. ext: filter
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_ExtWithDot_PreservesDot()
    {
        var query = SearchQueryParser.Parse("ext:.pdf");

        Assert.AreEqual(".pdf", query.ExtensionFilter);
    }

    [TestMethod]
    public void Parse_ExtWithoutDot_AddsDot()
    {
        var query = SearchQueryParser.Parse("ext:txt");

        Assert.AreEqual(".txt", query.ExtensionFilter);
    }

    [TestMethod]
    public void Parse_ExtCaseInsensitivePrefix_Works()
    {
        var query = SearchQueryParser.Parse("EXT:.docx");

        Assert.AreEqual(".docx", query.ExtensionFilter);
    }

    // -------------------------------------------------------
    // 9. Combined queries
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_KindAndNameCombined_BothSet()
    {
        var query = SearchQueryParser.Parse("kind:image photo");

        Assert.AreEqual(FileKind.Image, query.KindFilter);
        Assert.AreEqual("photo", query.NameFilter);
    }

    [TestMethod]
    public void Parse_AllFiltersCombined_AllSet()
    {
        var query = SearchQueryParser.Parse("kind:video size:>1MB ext:.mp4 vacation");

        Assert.AreEqual(FileKind.Video, query.KindFilter);
        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(1L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
        Assert.AreEqual(".mp4", query.ExtensionFilter);
        Assert.AreEqual("vacation", query.NameFilter);
    }

    [TestMethod]
    public void Parse_MultipleNameTokensWithFilter_NameTokensJoined()
    {
        var query = SearchQueryParser.Parse("my vacation kind:image photos");

        Assert.AreEqual(FileKind.Image, query.KindFilter);
        Assert.AreEqual("my vacation photos", query.NameFilter);
    }

    [TestMethod]
    public void Parse_SizeAndDateCombined_BothSet()
    {
        var query = SearchQueryParser.Parse("size:large date:thisweek");
        var today = DateTime.Now.Date;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(128L * 1024 * 1024, query.SizeFilter!.Value.Bytes);

        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
        Assert.AreEqual(today.AddDays(-daysSinceMonday), query.DateFilter!.Value.Date);
    }

    // -------------------------------------------------------
    // 10. GetExtensionsForKind
    // -------------------------------------------------------

    [TestMethod]
    public void GetExtensionsForKind_Image_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Image);

        Assert.IsTrue(extensions.Contains(".jpg"));
        Assert.IsTrue(extensions.Contains(".jpeg"));
        Assert.IsTrue(extensions.Contains(".png"));
        Assert.IsTrue(extensions.Contains(".gif"));
        Assert.IsTrue(extensions.Contains(".bmp"));
        Assert.IsTrue(extensions.Contains(".webp"));
        Assert.IsTrue(extensions.Contains(".svg"));
        Assert.IsTrue(extensions.Contains(".heic"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Video_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Video);

        Assert.IsTrue(extensions.Contains(".mp4"));
        Assert.IsTrue(extensions.Contains(".avi"));
        Assert.IsTrue(extensions.Contains(".mkv"));
        Assert.IsTrue(extensions.Contains(".mov"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Audio_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Audio);

        Assert.IsTrue(extensions.Contains(".mp3"));
        Assert.IsTrue(extensions.Contains(".wav"));
        Assert.IsTrue(extensions.Contains(".flac"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Document_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Document);

        Assert.IsTrue(extensions.Contains(".pdf"));
        Assert.IsTrue(extensions.Contains(".docx"));
        Assert.IsTrue(extensions.Contains(".xlsx"));
        Assert.IsTrue(extensions.Contains(".txt"));
        Assert.IsTrue(extensions.Contains(".md"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Archive_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Archive);

        Assert.IsTrue(extensions.Contains(".zip"));
        Assert.IsTrue(extensions.Contains(".rar"));
        Assert.IsTrue(extensions.Contains(".7z"));
        Assert.IsTrue(extensions.Contains(".tar"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Code_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Code);

        Assert.IsTrue(extensions.Contains(".cs"));
        Assert.IsTrue(extensions.Contains(".js"));
        Assert.IsTrue(extensions.Contains(".py"));
        Assert.IsTrue(extensions.Contains(".html"));
        Assert.IsTrue(extensions.Contains(".json"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Executable_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Executable);

        Assert.IsTrue(extensions.Contains(".exe"));
        Assert.IsTrue(extensions.Contains(".msi"));
        Assert.IsTrue(extensions.Contains(".dll"));
    }

    [TestMethod]
    public void GetExtensionsForKind_Font_ContainsExpectedExtensions()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Font);

        Assert.IsTrue(extensions.Contains(".ttf"));
        Assert.IsTrue(extensions.Contains(".otf"));
        Assert.IsTrue(extensions.Contains(".woff"));
        Assert.IsTrue(extensions.Contains(".woff2"));
    }

    [TestMethod]
    public void GetExtensionsForKind_ExtensionLookup_IsCaseInsensitive()
    {
        var extensions = SearchQueryParser.GetExtensionsForKind(FileKind.Image);

        // The HashSet uses OrdinalIgnoreCase, so ".JPG" should match ".jpg"
        Assert.IsTrue(extensions.Contains(".JPG"));
        Assert.IsTrue(extensions.Contains(".Png"));
    }

    // -------------------------------------------------------
    // 11. Edge cases
    // -------------------------------------------------------

    [TestMethod]
    public void Parse_KindWithEmptyValue_TreatedAsNameToken()
    {
        // "kind:" with nothing after -> token is "kind:", TryParseKind
        // returns false because value is empty -> becomes name token
        var query = SearchQueryParser.Parse("kind:");

        Assert.IsNull(query.KindFilter);
        Assert.AreEqual("kind:", query.NameFilter);
    }

    [TestMethod]
    public void Parse_SizeWithEmptyValue_TreatedAsNameToken()
    {
        var query = SearchQueryParser.Parse("size:");

        Assert.IsNull(query.SizeFilter);
        Assert.AreEqual("size:", query.NameFilter);
    }

    [TestMethod]
    public void Parse_DateWithInvalidFormat_TreatedAsNameToken()
    {
        var query = SearchQueryParser.Parse("date:notadate");

        Assert.IsNull(query.DateFilter);
        Assert.AreEqual("date:notadate", query.NameFilter);
    }

    [TestMethod]
    public void Parse_ExtWithEmptyValue_TreatedAsNameToken()
    {
        var query = SearchQueryParser.Parse("ext:");

        Assert.IsNull(query.ExtensionFilter);
        Assert.AreEqual("ext:", query.NameFilter);
    }

    [TestMethod]
    public void Parse_SizeDecimalValue_Parsed()
    {
        var query = SearchQueryParser.Parse("size:>1.5MB");

        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual((long)(1.5 * 1024 * 1024), query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void Parse_OnlyFilters_NoNameFilter()
    {
        var query = SearchQueryParser.Parse("kind:image size:large ext:.jpg");

        Assert.IsNull(query.NameFilter);
        Assert.AreEqual(FileKind.Image, query.KindFilter);
        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(".jpg", query.ExtensionFilter);
        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void Parse_QuotedStringWithFilter_BothParsed()
    {
        var query = SearchQueryParser.Parse("kind:document \"annual report\"");

        Assert.AreEqual(FileKind.Document, query.KindFilter);
        Assert.AreEqual("annual report", query.NameFilter);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_TrueWhenAllNull()
    {
        var query = new SearchQuery();

        Assert.IsTrue(query.IsEmpty);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_FalseWhenNameFilterSet()
    {
        var query = new SearchQuery { NameFilter = "test" };

        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_FalseWhenKindFilterSet()
    {
        var query = new SearchQuery { KindFilter = FileKind.Image };

        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_FalseWhenSizeFilterSet()
    {
        var query = new SearchQuery { SizeFilter = (CompareOp.GreaterThan, 100L) };

        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_FalseWhenDateFilterSet()
    {
        var query = new SearchQuery { DateFilter = (CompareOp.GreaterOrEqual, DateTime.Now) };

        Assert.IsFalse(query.IsEmpty);
    }

    [TestMethod]
    public void SearchQuery_IsEmpty_FalseWhenExtensionFilterSet()
    {
        var query = new SearchQuery { ExtensionFilter = ".txt" };

        Assert.IsFalse(query.IsEmpty);
    }
}
