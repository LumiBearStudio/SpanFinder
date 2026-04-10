using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class PreviewTypeTests
{
    [TestMethod]
    public void AllPreviewTypes_AreDefined()
    {
        // Verify all expected preview types exist (matches FEATURES.md)
        Assert.AreEqual(0, (int)PreviewType.None);
        Assert.AreEqual(1, (int)PreviewType.Image);
        Assert.AreEqual(2, (int)PreviewType.Text);
        Assert.AreEqual(3, (int)PreviewType.Pdf);
        Assert.AreEqual(4, (int)PreviewType.Media);
        Assert.AreEqual(5, (int)PreviewType.Folder);
        Assert.AreEqual(6, (int)PreviewType.HexBinary);
        Assert.AreEqual(7, (int)PreviewType.Font);
        Assert.AreEqual(8, (int)PreviewType.Archive);
        Assert.AreEqual(9, (int)PreviewType.Markdown);
        Assert.AreEqual(10, (int)PreviewType.Csv);
        Assert.AreEqual(11, (int)PreviewType.Generic);
    }

    [TestMethod]
    public void PreviewType_TotalCount_Is12()
    {
        var values = Enum.GetValues<PreviewType>();
        Assert.AreEqual(12, values.Length,
            "PreviewType should have 12 values: None, Image, Text, Pdf, Media, Folder, HexBinary, Font, Archive, Markdown, Csv, Generic");
    }

    [TestMethod]
    [DataRow(PreviewType.None, "None")]
    [DataRow(PreviewType.Image, "Image")]
    [DataRow(PreviewType.Text, "Text")]
    [DataRow(PreviewType.Pdf, "Pdf")]
    [DataRow(PreviewType.Media, "Media")]
    [DataRow(PreviewType.Folder, "Folder")]
    [DataRow(PreviewType.HexBinary, "HexBinary")]
    [DataRow(PreviewType.Font, "Font")]
    [DataRow(PreviewType.Archive, "Archive")]
    [DataRow(PreviewType.Markdown, "Markdown")]
    [DataRow(PreviewType.Csv, "Csv")]
    [DataRow(PreviewType.Generic, "Generic")]
    public void PreviewType_ToString_ReturnsExpectedName(PreviewType type, string expectedName)
    {
        Assert.AreEqual(expectedName, type.ToString());
    }

    [TestMethod]
    public void PreviewType_Parse_Works()
    {
        Assert.AreEqual(PreviewType.Image, Enum.Parse<PreviewType>("Image"));
        Assert.AreEqual(PreviewType.HexBinary, Enum.Parse<PreviewType>("HexBinary"));
        Assert.AreEqual(PreviewType.Font, Enum.Parse<PreviewType>("Font"));
    }
}
