using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class PathTextFormatterTests
{
    // ── FormatQuoted ("경로로 복사", Ctrl+Shift+C — Windows 11 탐색기 형식) ──

    [TestMethod]
    public void Quoted_Single_AlwaysQuoted_EvenWithoutSpaces()
        => Assert.AreEqual("\"D:\\Dev\\.gitignore\"", PathTextFormatter.FormatQuoted(new[] { @"D:\Dev\.gitignore" }));

    [TestMethod]
    public void Quoted_Multiple_CrlfSeparated_NoTrailingNewline()
        => Assert.AreEqual("\"C:\\a b.txt\"\r\n\"C:\\한글 파일.txt\"",
            PathTextFormatter.FormatQuoted(new[] { @"C:\a b.txt", @"C:\한글 파일.txt" }));

    [TestMethod]
    public void Quoted_KeepsGivenOrder()
        => Assert.AreEqual("\"C:\\c\"\r\n\"C:\\a\"", PathTextFormatter.FormatQuoted(new[] { @"C:\c", @"C:\a" }));

    [TestMethod]
    public void Quoted_SkipsEmptyEntries()
        => Assert.AreEqual("\"C:\\a\"", PathTextFormatter.FormatQuoted(new[] { "", @"C:\a", "  ", null! }));

    [TestMethod]
    public void Quoted_NothingToCopy_ReturnsEmpty()
    {
        Assert.AreEqual("", PathTextFormatter.FormatQuoted(Array.Empty<string>()));
        Assert.AreEqual("", PathTextFormatter.FormatQuoted(null!));
    }

    [TestMethod]
    public void Quoted_ArchiveEntry_ExplorerForm()
        => Assert.AreEqual("\"D:\\x\\a.zip\\dir\\f.txt\"",
            PathTextFormatter.FormatQuoted(new[] { @"archive://D:\x\a.zip/dir/f.txt" }));

    [TestMethod]
    public void Quoted_RemoteUri_Unchanged()
        => Assert.AreEqual("\"sftp://user@host:22/home/f.txt\"",
            PathTextFormatter.FormatQuoted(new[] { "sftp://user@host:22/home/f.txt" }));

    // ── FormatPlain (우클릭 "경로 복사" — 따옴표 없음) ──

    [TestMethod]
    public void Plain_Single_IsUnchangedPath()
        => Assert.AreEqual(@"C:\a b.txt", PathTextFormatter.FormatPlain(new[] { @"C:\a b.txt" }));

    [TestMethod]
    public void Plain_ArchiveEntry_ExplorerForm()
        => Assert.AreEqual(@"D:\x\a.zip\dir\f.txt", PathTextFormatter.FormatPlain(new[] { @"archive://D:\x\a.zip/dir/f.txt" }));

    [TestMethod]
    public void ToDisplayPath_ArchiveRoot_IsArchiveFile()
        => Assert.AreEqual(@"D:\x\a.zip", PathTextFormatter.ToDisplayPath(@"archive://D:\x\a.zip"));

    [TestMethod]
    public void ToDisplayPath_NormalPath_Unchanged()
        => Assert.AreEqual(@"\\srv\share\a b.txt", PathTextFormatter.ToDisplayPath(@"\\srv\share\a b.txt"));

    [TestMethod]
    public void Plain_Multiple_CrlfSeparated()
        => Assert.AreEqual("C:\\a\r\nC:\\b", PathTextFormatter.FormatPlain(new[] { @"C:\a", @"C:\b" }));

    // ── StripEnclosingQuotes (주소창 입력) ──

    [TestMethod]
    public void Strip_QuotedPath_RemovesQuotes()
        => Assert.AreEqual(@"C:\My Folder", PathTextFormatter.StripEnclosingQuotes("\"C:\\My Folder\""));

    [TestMethod]
    public void Strip_QuotedPathWithOuterSpaces_RemovesQuotes()
        => Assert.AreEqual(@"C:\My Folder", PathTextFormatter.StripEnclosingQuotes("  \"C:\\My Folder\"  "));

    [TestMethod]
    public void Strip_UnquotedPath_Unchanged()
        => Assert.AreEqual(@"C:\My Folder", PathTextFormatter.StripEnclosingQuotes(@"C:\My Folder"));

    [TestMethod]
    public void Strip_TwoQuotedPaths_Unchanged()
    {
        const string input = "\"C:\\a\" \"C:\\b\"";
        Assert.AreEqual(input, PathTextFormatter.StripEnclosingQuotes(input));
    }

    [TestMethod]
    public void Strip_OnlyLeadingQuote_Unchanged()
        => Assert.AreEqual("\"C:\\a", PathTextFormatter.StripEnclosingQuotes("\"C:\\a"));

    [TestMethod]
    public void Strip_EmptyQuotes_ReturnsEmpty()
        => Assert.AreEqual("", PathTextFormatter.StripEnclosingQuotes("\"\""));

    [TestMethod]
    public void Strip_ShellAndEnvironmentForms_Unchanged()
    {
        Assert.AreEqual("shell:downloads", PathTextFormatter.StripEnclosingQuotes("shell:downloads"));
        Assert.AreEqual(@"%USERPROFILE%\Desktop", PathTextFormatter.StripEnclosingQuotes("\"%USERPROFILE%\\Desktop\""));
    }

    [TestMethod]
    public void Strip_NullOrEmpty_Passthrough()
    {
        Assert.IsNull(PathTextFormatter.StripEnclosingQuotes(null!));
        Assert.AreEqual("", PathTextFormatter.StripEnclosingQuotes(""));
    }
}
