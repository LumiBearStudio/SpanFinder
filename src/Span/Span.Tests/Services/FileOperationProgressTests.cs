namespace Span.Tests.Services;

[TestClass]
public class FileOperationProgressTests
{
    // ── Default values ──────────────────────────────────

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress();

        Assert.AreEqual(string.Empty, p.CurrentFile);
        Assert.AreEqual(0L, p.TotalBytes);
        Assert.AreEqual(0L, p.ProcessedBytes);
        Assert.AreEqual(0, p.Percentage);
        Assert.AreEqual(0.0, p.SpeedBytesPerSecond);
        Assert.AreEqual(TimeSpan.Zero, p.EstimatedTimeRemaining);
        Assert.AreEqual(0, p.CurrentFileIndex);
        Assert.AreEqual(0, p.TotalFileCount);
    }

    // ── Percentage computation ──────────────────────────

    [TestMethod]
    public void Percentage_ComputedFromBytes_WhenNotExplicitlySet()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 200,
            ProcessedBytes = 100
        };

        Assert.AreEqual(50, p.Percentage);
    }

    [TestMethod]
    public void Percentage_ZeroTotalBytes_ReturnsZero()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 0,
            ProcessedBytes = 500
        };

        Assert.AreEqual(0, p.Percentage);
    }

    [TestMethod]
    public void Percentage_ProcessedExceedsTotal_ReturnsOverHundred()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 100,
            ProcessedBytes = 150
        };

        // 150 * 100 / 100 = 150
        Assert.AreEqual(150, p.Percentage);
    }

    [TestMethod]
    public void Percentage_ExplicitlySet_OverridesComputation()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 200,
            ProcessedBytes = 100
        };
        p.Percentage = 75;

        Assert.AreEqual(75, p.Percentage);
    }

    [TestMethod]
    public void Percentage_FullCompletion_Returns100()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 1024,
            ProcessedBytes = 1024
        };

        Assert.AreEqual(100, p.Percentage);
    }

    [TestMethod]
    public void Percentage_SmallFraction_TruncatesToInt()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 3,
            ProcessedBytes = 1
        };

        // 1 * 100 / 3 = 33 (integer division)
        Assert.AreEqual(33, p.Percentage);
    }

    [TestMethod]
    public void Percentage_LargeBytes_ComputesCorrectly()
    {
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            TotalBytes = 10L * 1024 * 1024 * 1024, // 10 GB
            ProcessedBytes = 5L * 1024 * 1024 * 1024 // 5 GB
        };

        Assert.AreEqual(50, p.Percentage);
    }

    // ── Other properties ────────────────────────────────

    [TestMethod]
    public void Properties_CanBeSetAndRead()
    {
        var eta = TimeSpan.FromMinutes(5);
        var p = new Span.Services.FileOperations.FileOperationProgress
        {
            CurrentFile = @"C:\docs\report.pdf",
            TotalBytes = 2048,
            ProcessedBytes = 512,
            SpeedBytesPerSecond = 1024.5,
            EstimatedTimeRemaining = eta,
            CurrentFileIndex = 3,
            TotalFileCount = 10
        };

        Assert.AreEqual(@"C:\docs\report.pdf", p.CurrentFile);
        Assert.AreEqual(2048L, p.TotalBytes);
        Assert.AreEqual(512L, p.ProcessedBytes);
        Assert.AreEqual(1024.5, p.SpeedBytesPerSecond);
        Assert.AreEqual(eta, p.EstimatedTimeRemaining);
        Assert.AreEqual(3, p.CurrentFileIndex);
        Assert.AreEqual(10, p.TotalFileCount);
    }
}
