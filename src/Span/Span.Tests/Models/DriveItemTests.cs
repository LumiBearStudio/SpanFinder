namespace Span.Tests.Models;

[TestClass]
public class DriveItemTests
{
    [TestMethod]
    public void UsagePercent_WithValidSizes_ReturnsCorrectPercentage()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 1000,
            AvailableFreeSpace = 300
        };

        Assert.AreEqual(70.0, drive.UsagePercent);
    }

    [TestMethod]
    public void UsagePercent_WhenTotalSizeIsZero_ReturnsZero()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 0,
            AvailableFreeSpace = 0
        };

        Assert.AreEqual(0.0, drive.UsagePercent);
    }

    [TestMethod]
    public void UsagePercent_WhenFullyUsed_Returns100()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 500_000_000_000,
            AvailableFreeSpace = 0
        };

        Assert.AreEqual(100.0, drive.UsagePercent);
    }

    [TestMethod]
    public void UsagePercent_WhenEmpty_Returns0()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 500_000_000_000,
            AvailableFreeSpace = 500_000_000_000
        };

        Assert.AreEqual(0.0, drive.UsagePercent);
    }

    [TestMethod]
    public void SizeDescription_WithGBSizes_FormatsCorrectly()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 500L * 1024 * 1024 * 1024, // 500 GB
            AvailableFreeSpace = 200L * 1024 * 1024 * 1024 // 200 GB
        };

        Assert.AreEqual("200.0 GB free of 500.0 GB", drive.SizeDescription);
    }

    [TestMethod]
    public void SizeDescription_WithTBSizes_FormatsCorrectly()
    {
        var drive = new Span.Models.DriveItem
        {
            TotalSize = 2L * 1024 * 1024 * 1024 * 1024, // 2 TB
            AvailableFreeSpace = 1L * 1024 * 1024 * 1024 * 1024 // 1 TB
        };

        Assert.AreEqual("1.0 TB free of 2.0 TB", drive.SizeDescription);
    }

    [TestMethod]
    public void SizeDescription_WhenTotalSizeIsZero_ReturnsEmpty()
    {
        var drive = new Span.Models.DriveItem { TotalSize = 0 };

        Assert.AreEqual(string.Empty, drive.SizeDescription);
    }

    [TestMethod]
    public void IsNetworkDrive_WhenDriveTypeIsNetwork_ReturnsTrue()
    {
        var drive = new Span.Models.DriveItem { DriveType = "Network" };

        Assert.IsTrue(drive.IsNetworkDrive);
    }

    [TestMethod]
    public void IsNetworkDrive_WhenDriveTypeIsFixed_ReturnsFalse()
    {
        var drive = new Span.Models.DriveItem { DriveType = "Fixed" };

        Assert.IsFalse(drive.IsNetworkDrive);
    }

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var drive = new Span.Models.DriveItem();

        Assert.AreEqual(string.Empty, drive.Name);
        Assert.AreEqual(string.Empty, drive.Path);
        Assert.AreEqual(string.Empty, drive.Label);
        Assert.AreEqual(string.Empty, drive.DriveFormat);
        Assert.AreEqual(string.Empty, drive.DriveType);
        Assert.AreEqual("\uEEA1", drive.IconGlyph);
        Assert.AreEqual(0L, drive.TotalSize);
        Assert.AreEqual(0L, drive.AvailableFreeSpace);
    }
}
