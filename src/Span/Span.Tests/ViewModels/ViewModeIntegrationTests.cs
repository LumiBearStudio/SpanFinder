using Span.Models;
using Span.Helpers;

namespace Span.Tests.ViewModels;

[TestClass]
public class ViewModeIntegrationTests
{
    [TestMethod]
    public void ViewMode_Miller_Enum()
    {
        Assert.AreEqual(0, (int)ViewMode.MillerColumns);
        Assert.IsTrue(Enum.IsDefined(typeof(ViewMode), ViewMode.MillerColumns));
    }

    [TestMethod]
    public void ViewMode_Details_Enum()
    {
        Assert.AreEqual(1, (int)ViewMode.Details);
        Assert.IsTrue(Enum.IsDefined(typeof(ViewMode), ViewMode.Details));
    }

    [TestMethod]
    public void ViewMode_Icons_SubModes()
    {
        Assert.AreEqual(2, (int)ViewMode.IconSmall);
        Assert.AreEqual(3, (int)ViewMode.IconMedium);
        Assert.AreEqual(4, (int)ViewMode.IconLarge);
        Assert.AreEqual(5, (int)ViewMode.IconExtraLarge);

        // All icon modes should report as icon mode
        Assert.IsTrue(ViewMode.IconSmall.IsIconMode());
        Assert.IsTrue(ViewMode.IconMedium.IsIconMode());
        Assert.IsTrue(ViewMode.IconLarge.IsIconMode());
        Assert.IsTrue(ViewMode.IconExtraLarge.IsIconMode());

        // Non-icon modes should not
        Assert.IsFalse(ViewMode.MillerColumns.IsIconMode());
        Assert.IsFalse(ViewMode.Details.IsIconMode());
        Assert.IsFalse(ViewMode.Home.IsIconMode());
        Assert.IsFalse(ViewMode.Settings.IsIconMode());
        Assert.IsFalse(ViewMode.List.IsIconMode());
    }

    [TestMethod]
    public void GetIconPixelSize_ReturnsCorrectSizes()
    {
        Assert.AreEqual(16, ViewMode.IconSmall.GetIconPixelSize());
        Assert.AreEqual(48, ViewMode.IconMedium.GetIconPixelSize());
        Assert.AreEqual(96, ViewMode.IconLarge.GetIconPixelSize());
        Assert.AreEqual(256, ViewMode.IconExtraLarge.GetIconPixelSize());

        // Non-icon modes return default (48)
        Assert.AreEqual(48, ViewMode.MillerColumns.GetIconPixelSize());
        Assert.AreEqual(48, ViewMode.Details.GetIconPixelSize());
    }
}
