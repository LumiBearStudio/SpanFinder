using Span.Helpers;
using Span.Models;

namespace Span.Tests.Helpers;

[TestClass]
public class ViewModeExtensionsTests
{
    [TestMethod]
    [DataRow(ViewMode.IconSmall, true)]
    [DataRow(ViewMode.IconMedium, true)]
    [DataRow(ViewMode.IconLarge, true)]
    [DataRow(ViewMode.IconExtraLarge, true)]
    [DataRow(ViewMode.MillerColumns, false)]
    [DataRow(ViewMode.Details, false)]
    [DataRow(ViewMode.Home, false)]
    public void IsIconMode_ReturnsExpected(ViewMode mode, bool expected)
    {
        Assert.AreEqual(expected, mode.IsIconMode());
    }

    [TestMethod]
    [DataRow(ViewMode.IconSmall, 16)]
    [DataRow(ViewMode.IconMedium, 48)]
    [DataRow(ViewMode.IconLarge, 96)]
    [DataRow(ViewMode.IconExtraLarge, 256)]
    [DataRow(ViewMode.MillerColumns, 48)] // Default
    [DataRow(ViewMode.Details, 48)] // Default
    public void GetIconPixelSize_ReturnsExpected(ViewMode mode, int expected)
    {
        Assert.AreEqual(expected, mode.GetIconPixelSize());
    }

    [TestMethod]
    [DataRow(ViewMode.MillerColumns, "Miller Columns")]
    [DataRow(ViewMode.Details, "Details")]
    [DataRow(ViewMode.IconSmall, "Small Icons")]
    [DataRow(ViewMode.IconMedium, "Medium Icons")]
    [DataRow(ViewMode.IconLarge, "Large Icons")]
    [DataRow(ViewMode.IconExtraLarge, "Extra Large Icons")]
    [DataRow(ViewMode.Home, "Home")]
    public void GetDisplayName_ReturnsExpected(ViewMode mode, string expected)
    {
        Assert.AreEqual(expected, mode.GetDisplayName());
    }

    [TestMethod]
    [DataRow(ViewMode.MillerColumns, "Ctrl+1")]
    [DataRow(ViewMode.Details, "Ctrl+2")]
    [DataRow(ViewMode.IconSmall, "Ctrl+4")]
    [DataRow(ViewMode.IconMedium, "Ctrl+4")]
    [DataRow(ViewMode.IconLarge, "Ctrl+4")]
    [DataRow(ViewMode.IconExtraLarge, "Ctrl+4")]
    [DataRow(ViewMode.Home, "")]
    public void GetShortcutText_ReturnsExpected(ViewMode mode, string expected)
    {
        Assert.AreEqual(expected, mode.GetShortcutText());
    }

    [TestMethod]
    public void ViewMode_ListExists_WithValue8()
    {
        Assert.AreEqual(8, (int)ViewMode.List);
        Assert.IsTrue(Enum.IsDefined(typeof(ViewMode), ViewMode.List));
    }

    [TestMethod]
    public void ViewMode_SettingsExists_WithValue7()
    {
        Assert.AreEqual(7, (int)ViewMode.Settings);
        Assert.IsTrue(Enum.IsDefined(typeof(ViewMode), ViewMode.Settings));
    }

    [TestMethod]
    [DataRow(ViewMode.List, false)]
    [DataRow(ViewMode.Settings, false)]
    public void IsIconMode_NonIconModes_ReturnsFalse(ViewMode mode, bool expected)
    {
        Assert.AreEqual(expected, mode.IsIconMode());
    }

    [TestMethod]
    public void GetDisplayName_List_ReturnsList()
    {
        Assert.AreEqual("List", ViewMode.List.GetDisplayName());
    }

    [TestMethod]
    public void GetDisplayName_Settings_ReturnsSettings()
    {
        Assert.AreEqual("Settings", ViewMode.Settings.GetDisplayName());
    }

    [TestMethod]
    public void GetShortcutText_List_ReturnsCtrl3()
    {
        Assert.AreEqual("Ctrl+3", ViewMode.List.GetShortcutText());
    }

    [TestMethod]
    public void GetShortcutText_Settings_ReturnsEmpty()
    {
        Assert.AreEqual("", ViewMode.Settings.GetShortcutText());
    }
}
