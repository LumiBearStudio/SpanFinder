using Span.Helpers;
using Span.Models;

namespace Span.Tests.Helpers;

/// <summary>
/// Tests for SearchFilter matching logic.
/// Since FileSystemViewModel/FolderViewModel/FileViewModel depend on WinUI,
/// we test SearchFilter indirectly through SearchQueryParser parsing +
/// verifying the query structure that drives matching decisions.
/// Direct matching tests use the SearchQuery model's properties.
/// </summary>
[TestClass]
public class SearchFilterTests
{
    // -------------------------------------------------------
    // 1. Wildcard Regex matching behavior
    // -------------------------------------------------------

    [TestMethod]
    public void WildcardRegex_StarDotExe_MatchesExeFiles()
    {
        var query = SearchQueryParser.Parse("*.exe");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsTrue(query.NameRegex!.IsMatch("notepad.exe"));
        Assert.IsTrue(query.NameRegex!.IsMatch("CALC.EXE"));
        Assert.IsTrue(query.NameRegex!.IsMatch("My App.exe"));
        Assert.IsFalse(query.NameRegex!.IsMatch("notepad.exe.bak"));
        Assert.IsFalse(query.NameRegex!.IsMatch("notepad.txt"));
    }

    [TestMethod]
    public void WildcardRegex_StarDotStar_MatchesEverything()
    {
        var query = SearchQueryParser.Parse("*.*");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsTrue(query.NameRegex!.IsMatch("file.txt"));
        Assert.IsTrue(query.NameRegex!.IsMatch("a.b"));
        Assert.IsFalse(query.NameRegex!.IsMatch("noextension")); // no dot
    }

    [TestMethod]
    public void WildcardRegex_PrefixStar_MatchesPrefix()
    {
        var query = SearchQueryParser.Parse("report*");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsTrue(query.NameRegex!.IsMatch("report"));
        Assert.IsTrue(query.NameRegex!.IsMatch("report_2024.pdf"));
        Assert.IsTrue(query.NameRegex!.IsMatch("REPORT_ANNUAL.xlsx"));
        Assert.IsFalse(query.NameRegex!.IsMatch("my_report.pdf")); // doesn't start with "report"
    }

    [TestMethod]
    public void WildcardRegex_QuestionMark_MatchesSingleChar()
    {
        var query = SearchQueryParser.Parse("file?.txt");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsTrue(query.NameRegex!.IsMatch("file1.txt"));
        Assert.IsTrue(query.NameRegex!.IsMatch("fileA.txt"));
        Assert.IsFalse(query.NameRegex!.IsMatch("file12.txt"));
        Assert.IsFalse(query.NameRegex!.IsMatch("file.txt"));
    }

    // -------------------------------------------------------
    // 2. Multi-extension filter behavior
    // -------------------------------------------------------

    [TestMethod]
    public void MultiExtension_ParsedCorrectly()
    {
        var query = SearchQueryParser.Parse("ext:jpg;png;gif");

        Assert.AreEqual(".jpg;.png;.gif", query.ExtensionFilter);
        Assert.IsTrue(query.ExtensionFilter!.Contains(";"));
    }

    [TestMethod]
    public void MultiExtension_SingleExtension_NoDot()
    {
        var query = SearchQueryParser.Parse("ext:pdf");

        Assert.AreEqual(".pdf", query.ExtensionFilter);
        Assert.IsFalse(query.ExtensionFilter!.Contains(";"));
    }

    // -------------------------------------------------------
    // 3. Combined query structure verification
    // -------------------------------------------------------

    [TestMethod]
    public void CombinedQuery_WildcardAndKind_BothSet()
    {
        var query = SearchQueryParser.Parse("*.mp3 kind:audio");

        Assert.IsNotNull(query.NameRegex);
        Assert.AreEqual(FileKind.Audio, query.KindFilter);
        Assert.IsTrue(query.NameRegex!.IsMatch("song.mp3"));
    }

    [TestMethod]
    public void CombinedQuery_WildcardAndSize_BothSet()
    {
        var query = SearchQueryParser.Parse("*.zip size:>10MB");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsNotNull(query.SizeFilter);
        Assert.AreEqual(CompareOp.GreaterThan, query.SizeFilter!.Value.Op);
        Assert.AreEqual(10L * 1024 * 1024, query.SizeFilter!.Value.Bytes);
    }

    [TestMethod]
    public void CombinedQuery_WildcardAndDate_BothSet()
    {
        var query = SearchQueryParser.Parse("*.pdf date:thisweek");

        Assert.IsNotNull(query.NameRegex);
        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(CompareOp.GreaterOrEqual, query.DateFilter!.Value.Op);
    }

    [TestMethod]
    public void CombinedQuery_AllFilters_AllSet()
    {
        var query = SearchQueryParser.Parse("report* kind:document size:>1MB date:thismonth ext:.pdf");

        Assert.IsNotNull(query.NameRegex, "Wildcard should set NameRegex");
        Assert.AreEqual(FileKind.Document, query.KindFilter);
        Assert.IsNotNull(query.SizeFilter);
        Assert.IsNotNull(query.DateFilter);
        Assert.AreEqual(".pdf", query.ExtensionFilter);
    }

    // -------------------------------------------------------
    // 4. SearchQuery.IsEmpty with new NameRegex
    // -------------------------------------------------------

    [TestMethod]
    public void SearchQuery_WithNameRegex_NotEmpty()
    {
        var query = SearchQueryParser.Parse("*.txt");

        Assert.IsFalse(query.IsEmpty);
        Assert.IsNotNull(query.NameRegex);
    }

    [TestMethod]
    public void SearchQuery_PlainText_vs_Wildcard_DifferentBehavior()
    {
        var plainQuery = SearchQueryParser.Parse("exe");
        var wildcardQuery = SearchQueryParser.Parse("*.exe");

        // Plain text: contains match, no regex
        Assert.IsNull(plainQuery.NameRegex);
        Assert.AreEqual("exe", plainQuery.NameFilter);

        // Wildcard: full-name regex match
        Assert.IsNotNull(wildcardQuery.NameRegex);
        Assert.AreEqual("*.exe", wildcardQuery.NameFilter);

        // "exe" contains should match "myexe.txt" but "*.exe" regex should NOT
        Assert.IsTrue(wildcardQuery.NameRegex!.IsMatch("notepad.exe"));
        Assert.IsFalse(wildcardQuery.NameRegex!.IsMatch("myexe.txt"));
    }
}
