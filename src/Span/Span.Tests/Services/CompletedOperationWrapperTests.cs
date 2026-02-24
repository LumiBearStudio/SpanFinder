using Moq;
using Span.Services.FileOperations;

namespace Span.Tests.Services;

[TestClass]
public class CompletedOperationWrapperTests
{
    [TestMethod]
    public async Task ExecuteAsync_ReturnsCachedResult_WithoutCallingInner()
    {
        // Arrange
        var mockInner = new Mock<IFileOperation>();
        var cachedResult = OperationResult.CreateSuccess("C:\\file.txt");
        var wrapper = new CompletedOperationWrapper(mockInner.Object, cachedResult);

        // Act
        var result = await wrapper.ExecuteAsync();

        // Assert
        Assert.AreSame(cachedResult, result);
        mockInner.Verify(
            op => op.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithProgressAndToken_StillReturnsCachedResult()
    {
        // Arrange
        var mockInner = new Mock<IFileOperation>();
        var cachedResult = OperationResult.CreateFailure("test error");
        var wrapper = new CompletedOperationWrapper(mockInner.Object, cachedResult);
        var progress = new Progress<FileOperationProgress>();
        using var cts = new CancellationTokenSource();

        // Act
        var result = await wrapper.ExecuteAsync(progress, cts.Token);

        // Assert
        Assert.AreSame(cachedResult, result);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("test error", result.ErrorMessage);
    }

    [TestMethod]
    public async Task UndoAsync_DelegatesToInnerOperation()
    {
        // Arrange
        var expectedUndoResult = OperationResult.CreateSuccess("C:\\undone.txt");
        var mockInner = new Mock<IFileOperation>();
        mockInner.Setup(op => op.UndoAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expectedUndoResult);
        var cachedResult = OperationResult.CreateSuccess("C:\\file.txt");
        var wrapper = new CompletedOperationWrapper(mockInner.Object, cachedResult);

        // Act
        var result = await wrapper.UndoAsync();

        // Assert
        Assert.AreSame(expectedUndoResult, result);
        mockInner.Verify(op => op.UndoAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void CanUndo_DelegatesToInnerOperation_WhenTrue()
    {
        // Arrange
        var mockInner = new Mock<IFileOperation>();
        mockInner.Setup(op => op.CanUndo).Returns(true);
        var wrapper = new CompletedOperationWrapper(mockInner.Object, OperationResult.CreateSuccess());

        // Act & Assert
        Assert.IsTrue(wrapper.CanUndo);
    }

    [TestMethod]
    public void CanUndo_DelegatesToInnerOperation_WhenFalse()
    {
        // Arrange
        var mockInner = new Mock<IFileOperation>();
        mockInner.Setup(op => op.CanUndo).Returns(false);
        var wrapper = new CompletedOperationWrapper(mockInner.Object, OperationResult.CreateSuccess());

        // Act & Assert
        Assert.IsFalse(wrapper.CanUndo);
    }

    [TestMethod]
    public void Description_DelegatesToInnerOperation()
    {
        // Arrange
        var mockInner = new Mock<IFileOperation>();
        mockInner.Setup(op => op.Description).Returns("Copy 3 files");
        var wrapper = new CompletedOperationWrapper(mockInner.Object, OperationResult.CreateSuccess());

        // Act & Assert
        Assert.AreEqual("Copy 3 files", wrapper.Description);
    }
}
