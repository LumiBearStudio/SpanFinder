namespace Span.Tests.Models;

[TestClass]
public class FileItemTests
{
    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var file = new Span.Models.FileItem();

        Assert.AreEqual(string.Empty, file.Name);
        Assert.AreEqual(string.Empty, file.Path);
        Assert.AreEqual(string.Empty, file.FileType);
        Assert.AreEqual(0L, file.Size);
        Assert.AreEqual("\uEDC9", file.IconGlyph);
    }

    [TestMethod]
    public void ImplementsIFileSystemItem()
    {
        var file = new Span.Models.FileItem { Name = "test.txt", Path = @"C:\test.txt" };

        Span.Models.IFileSystemItem item = file;
        Assert.AreEqual("test.txt", item.Name);
        Assert.AreEqual(@"C:\test.txt", item.Path);
        Assert.AreEqual("\uEDC9", item.IconGlyph);
    }

    [TestMethod]
    public void Properties_CanBeSetAndRead()
    {
        var date = new DateTime(2025, 6, 15, 10, 30, 0);
        var file = new Span.Models.FileItem
        {
            Name = "document.pdf",
            Path = @"C:\Users\test\document.pdf",
            Size = 1024 * 1024, // 1 MB
            DateModified = date,
            FileType = ".pdf"
        };

        Assert.AreEqual("document.pdf", file.Name);
        Assert.AreEqual(@"C:\Users\test\document.pdf", file.Path);
        Assert.AreEqual(1024 * 1024, file.Size);
        Assert.AreEqual(date, file.DateModified);
        Assert.AreEqual(".pdf", file.FileType);
    }
}
