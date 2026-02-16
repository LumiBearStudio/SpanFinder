namespace Span.Tests.Models;

[TestClass]
public class TabItemTests
{
    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var tab = new Span.Models.TabItem();

        Assert.AreEqual(string.Empty, tab.Header);
        Assert.AreEqual(string.Empty, tab.Icon);
    }

    [TestMethod]
    public void Properties_CanBeSetAndRead()
    {
        var tab = new Span.Models.TabItem
        {
            Header = "Documents",
            Icon = "\uE8A5"
        };

        Assert.AreEqual("Documents", tab.Header);
        Assert.AreEqual("\uE8A5", tab.Icon);
    }
}
