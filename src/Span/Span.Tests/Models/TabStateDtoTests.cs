using System.Text.Json;
using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class TabStateDtoTests
{
    [TestMethod]
    public void Constructor_AssignsAllProperties()
    {
        var dto = new TabStateDto(
            Id: "abc12345",
            Header: "Documents",
            Path: @"C:\Users\u\Documents",
            ViewMode: 1,
            IconSize: 3);

        Assert.AreEqual("abc12345", dto.Id);
        Assert.AreEqual("Documents", dto.Header);
        Assert.AreEqual(@"C:\Users\u\Documents", dto.Path);
        Assert.AreEqual(1, dto.ViewMode);
        Assert.AreEqual(3, dto.IconSize);
    }

    [TestMethod]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new TabStateDto("id1", "h", "p", 0, 0);
        var b = new TabStateDto("id1", "h", "p", 0, 0);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void RecordEquality_DifferentValues_NotEqual()
    {
        var a = new TabStateDto("id1", "h", "p", 0, 0);
        var b = new TabStateDto("id1", "h", "p", 1, 0); // ViewMode 다름
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Json_RoundTrip_PreservesAllFields()
    {
        var original = new TabStateDto("xyz", "Tab Header", @"D:\Work", 2, 4);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<TabStateDto>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void Json_List_RoundTrip()
    {
        // MainViewModel.SaveTabsToJson은 List<TabStateDto>를 직렬화한다.
        var list = new List<TabStateDto>
        {
            new("id1", "Home", "", 6, 3),
            new("id2", "Code", @"C:\src", 0, 2),
            new("id3", "Pics", @"D:\Pictures", 4, 5),
        };

        var json = JsonSerializer.Serialize(list);
        var restored = JsonSerializer.Deserialize<List<TabStateDto>>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(3, restored.Count);
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(list[i], restored[i]);
    }

    [TestMethod]
    public void Json_HandlesEmptyStrings()
    {
        var dto = new TabStateDto(string.Empty, string.Empty, string.Empty, 0, 0);
        var json = JsonSerializer.Serialize(dto);
        var restored = JsonSerializer.Deserialize<TabStateDto>(json);
        Assert.AreEqual(dto, restored);
    }

    [TestMethod]
    public void Json_HandlesUnicodeAndPathSeparators()
    {
        var dto = new TabStateDto(
            Id: "한글ID",
            Header: "다운로드 / Downloads",
            Path: @"C:\사용자\문서\한글파일.txt",
            ViewMode: 0,
            IconSize: 0);

        var json = JsonSerializer.Serialize(dto);
        var restored = JsonSerializer.Deserialize<TabStateDto>(json);

        Assert.AreEqual(dto, restored);
    }

    [TestMethod]
    public void Deconstruct_PositionalRecord_Works()
    {
        var dto = new TabStateDto("a", "b", "c", 1, 2);
        var (id, header, path, viewMode, iconSize) = dto;
        Assert.AreEqual("a", id);
        Assert.AreEqual("b", header);
        Assert.AreEqual("c", path);
        Assert.AreEqual(1, viewMode);
        Assert.AreEqual(2, iconSize);
    }

    [TestMethod]
    public void With_ImmutableUpdate()
    {
        var dto = new TabStateDto("a", "b", "c", 0, 0);
        var updated = dto with { ViewMode = 5, IconSize = 3 };

        Assert.AreEqual(0, dto.ViewMode);  // 원본 불변
        Assert.AreEqual(5, updated.ViewMode);
        Assert.AreEqual(3, updated.IconSize);
        Assert.AreEqual("a", updated.Id);
    }
}
