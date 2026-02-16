using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class NaturalStringComparerTests
{
    private readonly NaturalStringComparer _comparer = NaturalStringComparer.Instance;

    [TestMethod]
    public void Compare_NullAndNull_ReturnsZero()
    {
        Assert.AreEqual(0, _comparer.Compare(null, null));
    }

    [TestMethod]
    public void Compare_NullAndValue_ReturnsNegative()
    {
        Assert.IsTrue(_comparer.Compare(null, "abc") < 0);
    }

    [TestMethod]
    public void Compare_ValueAndNull_ReturnsPositive()
    {
        Assert.IsTrue(_comparer.Compare("abc", null) > 0);
    }

    [TestMethod]
    public void Compare_SameStrings_ReturnsZero()
    {
        Assert.AreEqual(0, _comparer.Compare("test", "test"));
    }

    [TestMethod]
    public void Compare_NaturalOrdering_NumbersComparedNumerically()
    {
        // Natural sort: 1 < 2 < 10 (not lexicographic: 1 < 10 < 2)
        Assert.IsTrue(_comparer.Compare("file1.txt", "file2.txt") < 0);
        Assert.IsTrue(_comparer.Compare("file2.txt", "file10.txt") < 0);
        Assert.IsTrue(_comparer.Compare("file1.txt", "file10.txt") < 0);
    }

    [TestMethod]
    public void Compare_AlphabeticOrdering_WorksCorrectly()
    {
        Assert.IsTrue(_comparer.Compare("apple", "banana") < 0);
        Assert.IsTrue(_comparer.Compare("banana", "apple") > 0);
    }

    [TestMethod]
    public void Compare_MixedContent_SortsNaturally()
    {
        var items = new List<string>
        {
            "item10", "item2", "item1", "item20", "item3"
        };

        items.Sort(_comparer);

        CollectionAssert.AreEqual(
            new[] { "item1", "item2", "item3", "item10", "item20" },
            items);
    }

    [TestMethod]
    public void Compare_WindowsFileNames_SortsLikeExplorer()
    {
        var items = new List<string>
        {
            "Photo (10).jpg", "Photo (2).jpg", "Photo (1).jpg", "Photo (3).jpg"
        };

        items.Sort(_comparer);

        CollectionAssert.AreEqual(
            new[] { "Photo (1).jpg", "Photo (2).jpg", "Photo (3).jpg", "Photo (10).jpg" },
            items);
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        Assert.AreSame(NaturalStringComparer.Instance, NaturalStringComparer.Instance);
    }
}
