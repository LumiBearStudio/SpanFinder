namespace Span.Tests.Services;

[TestClass]
public class OperationResultTests
{
    // ── CreateSuccess ───────────────────────────────────

    [TestMethod]
    public void CreateSuccess_NoArgs_ReturnsSuccessWithEmptyPaths()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateSuccess();

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ErrorMessage);
        Assert.IsNotNull(result.AffectedPaths);
        Assert.AreEqual(0, result.AffectedPaths.Count);
    }

    [TestMethod]
    public void CreateSuccess_WithPaths_StoresAffectedPaths()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateSuccess(
            @"C:\file1.txt", @"C:\file2.txt");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.AffectedPaths.Count);
        Assert.AreEqual(@"C:\file1.txt", result.AffectedPaths[0]);
        Assert.AreEqual(@"C:\file2.txt", result.AffectedPaths[1]);
    }

    [TestMethod]
    public void CreateSuccess_SinglePath()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateSuccess(@"D:\temp\output.log");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.AffectedPaths.Count);
        Assert.AreEqual(@"D:\temp\output.log", result.AffectedPaths[0]);
    }

    // ── CreateFailure ───────────────────────────────────

    [TestMethod]
    public void CreateFailure_SetsErrorMessage()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateFailure("Access denied");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Access denied", result.ErrorMessage);
        Assert.IsNotNull(result.AffectedPaths);
        Assert.AreEqual(0, result.AffectedPaths.Count);
    }

    [TestMethod]
    public void CreateFailure_EmptyMessage()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateFailure("");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("", result.ErrorMessage);
    }

    // ── Default constructor ─────────────────────────────

    [TestMethod]
    public void DefaultConstructor_SuccessIsFalse()
    {
        var result = new Span.Services.FileOperations.OperationResult();

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.ErrorMessage);
        Assert.IsNotNull(result.AffectedPaths);
        Assert.AreEqual(0, result.AffectedPaths.Count);
    }

    // ── Mutability ──────────────────────────────────────

    [TestMethod]
    public void AffectedPaths_CanBeModifiedAfterCreation()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateSuccess();
        result.AffectedPaths.Add(@"C:\new.txt");

        Assert.AreEqual(1, result.AffectedPaths.Count);
    }

    [TestMethod]
    public void Properties_CanBeOverwritten()
    {
        var result = Span.Services.FileOperations.OperationResult.CreateSuccess(@"C:\a.txt");
        result.Success = false;
        result.ErrorMessage = "late failure";

        Assert.IsFalse(result.Success);
        Assert.AreEqual("late failure", result.ErrorMessage);
        Assert.AreEqual(1, result.AffectedPaths.Count);
    }
}
