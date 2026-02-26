using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class CloudStateTests
{
    [TestMethod]
    public void AllCloudStates_AreDefined()
    {
        // Verify all expected cloud states exist (matches FEATURES.md)
        Assert.IsTrue(Enum.IsDefined(typeof(CloudState), (int)CloudState.None));
        Assert.IsTrue(Enum.IsDefined(typeof(CloudState), (int)CloudState.Synced));
        Assert.IsTrue(Enum.IsDefined(typeof(CloudState), (int)CloudState.CloudOnly));
        Assert.IsTrue(Enum.IsDefined(typeof(CloudState), (int)CloudState.PendingUpload));
        Assert.IsTrue(Enum.IsDefined(typeof(CloudState), (int)CloudState.Syncing));
    }

    [TestMethod]
    public void CloudState_TotalCount_Is5()
    {
        var values = Enum.GetValues<CloudState>();
        Assert.AreEqual(5, values.Length,
            "CloudState should have 5 values: None, Synced, CloudOnly, PendingUpload, Syncing");
    }

    [TestMethod]
    [DataRow(CloudState.None, "None")]
    [DataRow(CloudState.Synced, "Synced")]
    [DataRow(CloudState.CloudOnly, "CloudOnly")]
    [DataRow(CloudState.PendingUpload, "PendingUpload")]
    [DataRow(CloudState.Syncing, "Syncing")]
    public void CloudState_ToString_ReturnsExpectedName(CloudState state, string expectedName)
    {
        Assert.AreEqual(expectedName, state.ToString());
    }

    [TestMethod]
    public void CloudState_DefaultValue_IsNone()
    {
        CloudState state = default;
        Assert.AreEqual(CloudState.None, state);
    }
}
