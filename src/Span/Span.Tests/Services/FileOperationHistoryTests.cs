using Moq;
using Span.Services.FileOperations;

namespace Span.Tests.Services;

[TestClass]
public class FileOperationHistoryTests
{
    private FileOperationHistory _history = null!;

    [TestInitialize]
    public void Setup()
    {
        _history = new FileOperationHistory();
    }

    private static Mock<IFileOperation> CreateSuccessOp(string description = "test op", bool canUndo = true)
    {
        var mock = new Mock<IFileOperation>();
        mock.Setup(o => o.Description).Returns(description);
        mock.Setup(o => o.CanUndo).Returns(canUndo);
        mock.Setup(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
        mock.Setup(o => o.UndoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
        return mock;
    }

    private static Mock<IFileOperation> CreateFailOp()
    {
        var mock = new Mock<IFileOperation>();
        mock.Setup(o => o.Description).Returns("fail op");
        mock.Setup(o => o.CanUndo).Returns(true);
        mock.Setup(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateFailure("operation failed"));
        return mock;
    }

    // ── ExecuteAsync ──

    [TestMethod]
    public async Task ExecuteAsync_SuccessfulOperation_AddedToUndoStack()
    {
        var op = CreateSuccessOp();

        var result = await _history.ExecuteAsync(op.Object);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(_history.CanUndo);
        Assert.AreEqual("test op", _history.UndoDescription);
    }

    [TestMethod]
    public async Task ExecuteAsync_FailedOperation_NotAddedToUndoStack()
    {
        var op = CreateFailOp();

        var result = await _history.ExecuteAsync(op.Object);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(_history.CanUndo);
        Assert.IsNull(_history.UndoDescription);
    }

    [TestMethod]
    public async Task ExecuteAsync_CanUndoFalse_NotAddedToUndoStack()
    {
        var op = CreateSuccessOp(canUndo: false);

        await _history.ExecuteAsync(op.Object);

        Assert.IsFalse(_history.CanUndo);
    }

    // ── UndoAsync ──

    [TestMethod]
    public async Task UndoAsync_ReversesLastOperation_MovesToRedoStack()
    {
        var op = CreateSuccessOp("move files");
        await _history.ExecuteAsync(op.Object);

        var result = await _history.UndoAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(_history.CanUndo);
        Assert.IsTrue(_history.CanRedo);
        Assert.AreEqual("move files", _history.RedoDescription);
        op.Verify(o => o.UndoAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UndoAsync_EmptyStack_ReturnsFailure()
    {
        var result = await _history.UndoAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Nothing to undo", result.ErrorMessage);
    }

    [TestMethod]
    public async Task UndoAsync_UndoFails_OperationRestoredToUndoStack()
    {
        var op = CreateSuccessOp();
        op.Setup(o => o.UndoAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(OperationResult.CreateFailure("undo failed"));
        await _history.ExecuteAsync(op.Object);

        var result = await _history.UndoAsync();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(_history.CanUndo, "실패 시 undo 스택에 복원되어야 함");
        Assert.IsFalse(_history.CanRedo);
    }

    // ── RedoAsync ──

    [TestMethod]
    public async Task RedoAsync_ReExecutesLastUndoneOperation()
    {
        var op = CreateSuccessOp("copy files");
        await _history.ExecuteAsync(op.Object);
        await _history.UndoAsync();

        var result = await _history.RedoAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(_history.CanUndo);
        Assert.IsFalse(_history.CanRedo);
        // ExecuteAsync는 총 2번 호출 (최초 실행 + redo)
        op.Verify(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task RedoAsync_EmptyStack_ReturnsFailure()
    {
        var result = await _history.RedoAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Nothing to redo", result.ErrorMessage);
    }

    [TestMethod]
    public async Task RedoAsync_RedoFails_OperationRestoredToRedoStack()
    {
        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);
        await _history.UndoAsync();

        // redo 시 실패하도록 설정
        op.Setup(o => o.ExecuteAsync(It.IsAny<IProgress<FileOperationProgress>?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(OperationResult.CreateFailure("redo failed"));

        var result = await _history.RedoAsync();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(_history.CanRedo, "실패 시 redo 스택에 복원되어야 함");
        Assert.IsFalse(_history.CanUndo);
    }

    // ── Execute after Undo clears redo stack ──

    [TestMethod]
    public async Task ExecuteAsync_AfterUndo_ClearsRedoStack()
    {
        var op1 = CreateSuccessOp("op1");
        await _history.ExecuteAsync(op1.Object);
        await _history.UndoAsync();
        Assert.IsTrue(_history.CanRedo);

        var op2 = CreateSuccessOp("op2");
        await _history.ExecuteAsync(op2.Object);

        Assert.IsFalse(_history.CanRedo, "새 실행 후 redo 스택이 비워져야 함");
        Assert.IsTrue(_history.CanUndo);
        Assert.AreEqual("op2", _history.UndoDescription);
    }

    // ── CanUndo / CanRedo 상태 확인 ──

    [TestMethod]
    public async Task CanUndo_CanRedo_CorrectStateAfterOperations()
    {
        Assert.IsFalse(_history.CanUndo);
        Assert.IsFalse(_history.CanRedo);

        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);
        Assert.IsTrue(_history.CanUndo);
        Assert.IsFalse(_history.CanRedo);

        await _history.UndoAsync();
        Assert.IsFalse(_history.CanUndo);
        Assert.IsTrue(_history.CanRedo);

        await _history.RedoAsync();
        Assert.IsTrue(_history.CanUndo);
        Assert.IsFalse(_history.CanRedo);
    }

    // ── MaxHistorySize ──

    [TestMethod]
    public async Task MaxHistorySize_OldestItemsRemovedWhenExceeded()
    {
        _history.MaxHistorySize = 3;

        for (int i = 0; i < 5; i++)
        {
            var op = CreateSuccessOp($"op{i}");
            await _history.ExecuteAsync(op.Object);
        }

        // 스택에는 최대 3개만 남아야 함
        int undoCount = 0;
        while (_history.CanUndo)
        {
            await _history.UndoAsync();
            undoCount++;
        }

        Assert.AreEqual(3, undoCount, "MaxHistorySize를 초과하면 오래된 항목이 제거되어야 함");
    }

    [TestMethod]
    public void MaxHistorySize_SetToZero_ClampsToOne()
    {
        _history.MaxHistorySize = 0;
        Assert.AreEqual(1, _history.MaxHistorySize);

        _history.MaxHistorySize = -5;
        Assert.AreEqual(1, _history.MaxHistorySize);
    }

    // ── Clear ──

    [TestMethod]
    public async Task Clear_EmptiesBothStacks()
    {
        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);
        await _history.UndoAsync();

        Assert.IsTrue(_history.CanRedo);

        // 다시 하나 실행하여 undo 스택에도 내용이 있게 함
        var op2 = CreateSuccessOp();
        await _history.ExecuteAsync(op2.Object);

        _history.Clear();

        Assert.IsFalse(_history.CanUndo);
        Assert.IsFalse(_history.CanRedo);
        Assert.IsNull(_history.UndoDescription);
        Assert.IsNull(_history.RedoDescription);
    }

    // ── HistoryChanged 이벤트 ──

    [TestMethod]
    public async Task HistoryChanged_FiredOnExecute()
    {
        var eventFired = false;
        _history.HistoryChanged += (_, args) =>
        {
            eventFired = true;
            Assert.IsTrue(args.CanUndo);
            Assert.IsFalse(args.CanRedo);
        };

        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);

        Assert.IsTrue(eventFired, "ExecuteAsync 후 HistoryChanged 이벤트가 발생해야 함");
    }

    [TestMethod]
    public async Task HistoryChanged_FiredOnUndo()
    {
        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);

        var eventFired = false;
        _history.HistoryChanged += (_, args) =>
        {
            eventFired = true;
            Assert.IsFalse(args.CanUndo);
            Assert.IsTrue(args.CanRedo);
        };

        await _history.UndoAsync();

        Assert.IsTrue(eventFired);
    }

    [TestMethod]
    public async Task HistoryChanged_FiredOnClear()
    {
        var op = CreateSuccessOp();
        await _history.ExecuteAsync(op.Object);

        var eventFired = false;
        _history.HistoryChanged += (_, args) =>
        {
            eventFired = true;
            Assert.IsFalse(args.CanUndo);
            Assert.IsFalse(args.CanRedo);
        };

        _history.Clear();

        Assert.IsTrue(eventFired);
    }

    [TestMethod]
    public async Task HistoryChanged_NotFiredOnFailedExecute()
    {
        var eventFired = false;
        _history.HistoryChanged += (_, _) => eventFired = true;

        var op = CreateFailOp();
        await _history.ExecuteAsync(op.Object);

        Assert.IsFalse(eventFired, "실패한 실행에서는 이벤트가 발생하지 않아야 함");
    }
}
