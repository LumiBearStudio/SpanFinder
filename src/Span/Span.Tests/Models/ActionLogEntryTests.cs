using Span.Models;
using System.Text.Json;

namespace Span.Tests.Models;

[TestClass]
public class ActionLogEntryTests
{
    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var entry = new ActionLogEntry();

        Assert.AreEqual(string.Empty, entry.OperationType);
        Assert.IsNotNull(entry.SourcePaths);
        Assert.AreEqual(0, entry.SourcePaths.Count);
        Assert.IsNull(entry.DestinationPath);
        Assert.IsFalse(entry.Success);
        Assert.IsNull(entry.ErrorMessage);
        Assert.AreEqual(string.Empty, entry.Description);
        Assert.AreEqual(0, entry.ItemCount);
    }

    [TestMethod]
    public void PropertyAssignment_Works()
    {
        var now = DateTime.Now;
        var entry = new ActionLogEntry
        {
            Timestamp = now,
            OperationType = "Copy",
            SourcePaths = new List<string> { @"C:\src\file.txt" },
            DestinationPath = @"C:\dest",
            Success = true,
            ErrorMessage = null,
            Description = "Copy file.txt",
            ItemCount = 1
        };

        Assert.AreEqual(now, entry.Timestamp);
        Assert.AreEqual("Copy", entry.OperationType);
        Assert.AreEqual(1, entry.SourcePaths.Count);
        Assert.AreEqual(@"C:\src\file.txt", entry.SourcePaths[0]);
        Assert.AreEqual(@"C:\dest", entry.DestinationPath);
        Assert.IsTrue(entry.Success);
        Assert.IsNull(entry.ErrorMessage);
        Assert.AreEqual("Copy file.txt", entry.Description);
        Assert.AreEqual(1, entry.ItemCount);
    }

    [TestMethod]
    public void JsonSerializationRoundTrip_PreservesData()
    {
        var entry = new ActionLogEntry
        {
            Timestamp = new DateTime(2026, 2, 26, 14, 30, 0),
            OperationType = "Move",
            SourcePaths = new List<string> { @"C:\a.txt", @"C:\b.txt" },
            DestinationPath = @"D:\backup",
            Success = true,
            Description = "Move 2 item(s)",
            ItemCount = 2
        };

        var json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<ActionLogEntry>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(entry.Timestamp, deserialized!.Timestamp);
        Assert.AreEqual(entry.OperationType, deserialized.OperationType);
        Assert.AreEqual(entry.SourcePaths.Count, deserialized.SourcePaths.Count);
        Assert.AreEqual(entry.SourcePaths[0], deserialized.SourcePaths[0]);
        Assert.AreEqual(entry.SourcePaths[1], deserialized.SourcePaths[1]);
        Assert.AreEqual(entry.DestinationPath, deserialized.DestinationPath);
        Assert.AreEqual(entry.Success, deserialized.Success);
        Assert.AreEqual(entry.Description, deserialized.Description);
        Assert.AreEqual(entry.ItemCount, deserialized.ItemCount);
    }

    [TestMethod]
    public void JsonSerializationRoundTrip_WithError()
    {
        var entry = new ActionLogEntry
        {
            Timestamp = DateTime.Now,
            OperationType = "Delete",
            SourcePaths = new List<string> { @"C:\locked.txt" },
            Success = false,
            ErrorMessage = "Access denied",
            Description = "Delete locked.txt",
            ItemCount = 1
        };

        var json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<ActionLogEntry>(json);

        Assert.IsNotNull(deserialized);
        Assert.IsFalse(deserialized!.Success);
        Assert.AreEqual("Access denied", deserialized.ErrorMessage);
    }

    [TestMethod]
    public void JsonDeserialization_EmptyArray_Works()
    {
        var json = "[]";
        var list = JsonSerializer.Deserialize<List<ActionLogEntry>>(json);

        Assert.IsNotNull(list);
        Assert.AreEqual(0, list!.Count);
    }

    [TestMethod]
    public void JsonDeserialization_MultipleEntries_Works()
    {
        var entries = new List<ActionLogEntry>
        {
            new() { OperationType = "Copy", Success = true, ItemCount = 3 },
            new() { OperationType = "Delete", Success = false, ErrorMessage = "fail" },
            new() { OperationType = "Rename", Success = true, ItemCount = 1 }
        };

        var json = JsonSerializer.Serialize(entries);
        var deserialized = JsonSerializer.Deserialize<List<ActionLogEntry>>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(3, deserialized!.Count);
        Assert.AreEqual("Copy", deserialized[0].OperationType);
        Assert.AreEqual("Delete", deserialized[1].OperationType);
        Assert.AreEqual("Rename", deserialized[2].OperationType);
    }

    [TestMethod]
    [DataRow("Copy")]
    [DataRow("Move")]
    [DataRow("Delete")]
    [DataRow("Rename")]
    [DataRow("NewFolder")]
    [DataRow("Compress")]
    [DataRow("Extract")]
    public void OperationType_AllKnownTypes_Assignable(string opType)
    {
        var entry = new ActionLogEntry { OperationType = opType };
        Assert.AreEqual(opType, entry.OperationType);
    }

    [TestMethod]
    public void MultipleSourcePaths_Preserved()
    {
        var paths = new List<string>
        {
            @"C:\file1.txt",
            @"C:\file2.txt",
            @"C:\dir\file3.txt",
            @"D:\another.doc"
        };

        var entry = new ActionLogEntry { SourcePaths = paths };

        Assert.AreEqual(4, entry.SourcePaths.Count);
        CollectionAssert.AreEqual(paths, entry.SourcePaths);
    }
}
