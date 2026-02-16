namespace Span.Tests.Models;

[TestClass]
public class ViewModeTests
{
    [TestMethod]
    public void ViewMode_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)Span.Models.ViewMode.MillerColumns);
        Assert.AreEqual(1, (int)Span.Models.ViewMode.Details);
        Assert.AreEqual(2, (int)Span.Models.ViewMode.IconSmall);
        Assert.AreEqual(3, (int)Span.Models.ViewMode.IconMedium);
        Assert.AreEqual(4, (int)Span.Models.ViewMode.IconLarge);
        Assert.AreEqual(5, (int)Span.Models.ViewMode.IconExtraLarge);
        Assert.AreEqual(6, (int)Span.Models.ViewMode.Home);
    }

    [TestMethod]
    public void PreviewType_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)Span.Models.PreviewType.None);
        Assert.AreEqual(1, (int)Span.Models.PreviewType.Image);
        Assert.AreEqual(2, (int)Span.Models.PreviewType.Text);
        Assert.AreEqual(3, (int)Span.Models.PreviewType.Pdf);
        Assert.AreEqual(4, (int)Span.Models.PreviewType.Media);
        Assert.AreEqual(5, (int)Span.Models.PreviewType.Folder);
        Assert.AreEqual(6, (int)Span.Models.PreviewType.Generic);
    }

    [TestMethod]
    public void ActivePane_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)Span.Models.ActivePane.Left);
        Assert.AreEqual(1, (int)Span.Models.ActivePane.Right);
    }
}
