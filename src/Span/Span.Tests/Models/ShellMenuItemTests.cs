namespace Span.Tests.Models;

[TestClass]
public class ShellMenuItemTests
{
    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var item = new Span.Models.ShellMenuItem();

        Assert.AreEqual(string.Empty, item.Text);
        Assert.AreEqual(string.Empty, item.Verb);
        Assert.AreEqual(0, item.CommandId);
        Assert.IsFalse(item.IsSeparator);
        Assert.IsFalse(item.IsDisabled);
        Assert.IsFalse(item.IsOwnerDrawn);
        Assert.IsNull(item.Children);
        Assert.IsFalse(item.HasSubmenu);
    }

    [TestMethod]
    public void HasSubmenu_WhenChildrenNull_ReturnsFalse()
    {
        var item = new Span.Models.ShellMenuItem { Children = null };

        Assert.IsFalse(item.HasSubmenu);
    }

    [TestMethod]
    public void HasSubmenu_WhenChildrenEmpty_ReturnsFalse()
    {
        var item = new Span.Models.ShellMenuItem { Children = new() };

        Assert.IsFalse(item.HasSubmenu);
    }

    [TestMethod]
    public void HasSubmenu_WhenHasChildren_ReturnsTrue()
    {
        var item = new Span.Models.ShellMenuItem
        {
            Children = new()
            {
                new Span.Models.ShellMenuItem { Text = "Child" }
            }
        };

        Assert.IsTrue(item.HasSubmenu);
    }

    [TestMethod]
    public void Properties_CanBeSetAndRead()
    {
        var item = new Span.Models.ShellMenuItem
        {
            Text = "Extract Here",
            CommandId = 42,
            Verb = "7-zip.extract",
            IsSeparator = false,
            IsDisabled = true,
            IsOwnerDrawn = true
        };

        Assert.AreEqual("Extract Here", item.Text);
        Assert.AreEqual(42, item.CommandId);
        Assert.AreEqual("7-zip.extract", item.Verb);
        Assert.IsFalse(item.IsSeparator);
        Assert.IsTrue(item.IsDisabled);
        Assert.IsTrue(item.IsOwnerDrawn);
    }
}
