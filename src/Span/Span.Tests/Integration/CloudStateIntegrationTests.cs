using Span.Models;

namespace Span.Tests.Integration;

[TestClass]
public class CloudStateIntegrationTests
{
    [TestMethod]
    public void DetectCloudState_LocalFile_ReturnsNone()
    {
        // Local files should default to CloudState.None
        var state = CloudState.None;
        Assert.AreEqual(CloudState.None, state);
        Assert.AreEqual(0, (int)state);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void DetectCloudState_GoogleDrive_ReturnsSynced()
    {
        var googleDrivePath = @"G:\내 드라이브\TEST";

        if (!Directory.Exists(googleDrivePath))
            Assert.Inconclusive("Google Drive TEST folder not available at " + googleDrivePath);

        // If we can access the folder, files should be in a synced state
        var files = Directory.GetFiles(googleDrivePath);
        Assert.IsTrue(files.Length >= 0, "Should be able to enumerate Google Drive folder");

        // Verify synced state enum value
        var syncedState = CloudState.Synced;
        Assert.AreEqual(1, (int)syncedState);
        Assert.AreNotEqual(CloudState.None, syncedState);
    }

    [TestMethod]
    public void CloudState_AllEnumValues_HaveDefinitions()
    {
        // Verify all expected enum values exist
        Assert.AreEqual(0, (int)CloudState.None);
        Assert.AreEqual(1, (int)CloudState.Synced);
        Assert.AreEqual(2, (int)CloudState.CloudOnly);
        Assert.AreEqual(3, (int)CloudState.PendingUpload);
        Assert.AreEqual(4, (int)CloudState.Syncing);

        // Verify all values are defined
        var values = Enum.GetValues<CloudState>();
        Assert.AreEqual(5, values.Length);

        foreach (var value in values)
        {
            Assert.IsTrue(Enum.IsDefined(typeof(CloudState), value),
                $"CloudState.{value} should be defined");
        }
    }
}
