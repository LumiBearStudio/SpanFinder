namespace Span.Tests.Models;

[TestClass]
public class FolderItemTests
{
    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var folder = new Span.Models.FolderItem();

        Assert.AreEqual(string.Empty, folder.Name);
        Assert.AreEqual(string.Empty, folder.Path);
        Assert.AreEqual("\uEEA7", folder.IconGlyph);
        Assert.IsNotNull(folder.Files);
        Assert.IsNotNull(folder.SubFolders);
        Assert.IsNotNull(folder.Children);
        Assert.AreEqual(0, folder.Files.Count);
        Assert.AreEqual(0, folder.SubFolders.Count);
        Assert.AreEqual(0, folder.Children.Count);
    }

    [TestMethod]
    public void ImplementsIFileSystemItem()
    {
        var folder = new Span.Models.FolderItem { Name = "Documents", Path = @"C:\Documents" };

        Span.Models.IFileSystemItem item = folder;
        Assert.AreEqual("Documents", item.Name);
        Assert.AreEqual(@"C:\Documents", item.Path);
        Assert.AreEqual("\uEEA7", item.IconGlyph);
    }

    [TestMethod]
    public void Collections_CanBePopulated()
    {
        var folder = new Span.Models.FolderItem { Name = "Root", Path = @"C:\" };

        folder.Files.Add(new Span.Models.FileItem { Name = "readme.txt" });
        folder.SubFolders.Add(new Span.Models.FolderItem { Name = "Sub" });

        Assert.AreEqual(1, folder.Files.Count);
        Assert.AreEqual(1, folder.SubFolders.Count);
        Assert.AreEqual("readme.txt", folder.Files[0].Name);
        Assert.AreEqual("Sub", folder.SubFolders[0].Name);
    }
}
