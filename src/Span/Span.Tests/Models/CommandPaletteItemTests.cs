using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class CommandPaletteItemTests
{
    [TestMethod]
    public void DefaultValues_AreSafeEmptyStrings()
    {
        var item = new CommandPaletteItem();
        Assert.AreEqual(string.Empty, item.Title);
        Assert.AreEqual(string.Empty, item.Category);
        Assert.AreEqual(string.Empty, item.GroupName);
        Assert.AreEqual(string.Empty, item.IconGlyph);
        Assert.AreEqual(string.Empty, item.Shortcut);
        Assert.AreEqual(string.Empty, item.CommandId);
        Assert.AreEqual(string.Empty, item.Path);
        Assert.AreEqual(-1, item.TabIndex);
        Assert.AreEqual(string.Empty, item.SettingKey);
        Assert.IsNull(item.SettingValue);
        Assert.AreEqual(string.Empty, item.CurrentStateText);
        Assert.IsTrue(item.IsEnabled);
    }

    [TestMethod]
    public void Aliases_StartsEmpty_AndIsMutable()
    {
        var item = new CommandPaletteItem();
        Assert.AreEqual(0, item.Aliases.Count);
        item.Aliases.Add("복사");
        item.Aliases.Add("copy");
        Assert.AreEqual(2, item.Aliases.Count);
    }

    [TestMethod]
    public void Opacity_Enabled_Is1()
    {
        var item = new CommandPaletteItem { IsEnabled = true };
        Assert.AreEqual(1.0, item.Opacity);
    }

    [TestMethod]
    public void Opacity_Disabled_Is04()
    {
        var item = new CommandPaletteItem { IsEnabled = false };
        Assert.AreEqual(0.4, item.Opacity);
    }

    [TestMethod]
    public void Type_Default_IsCommand()
    {
        var item = new CommandPaletteItem();
        Assert.AreEqual(CommandPaletteItemType.Command, item.Type);
    }

    [TestMethod]
    public void Type_AllValues_DefinedInExpectedOrder()
    {
        Assert.AreEqual(0, (int)CommandPaletteItemType.Command);
        Assert.AreEqual(1, (int)CommandPaletteItemType.Tab);
        Assert.AreEqual(2, (int)CommandPaletteItemType.Navigation);
        Assert.AreEqual(3, (int)CommandPaletteItemType.SettingToggle);
        Assert.AreEqual(4, (int)CommandPaletteItemType.SettingSelect);
        Assert.AreEqual(5, (int)CommandPaletteItemType.SettingsSection);
    }

    [TestMethod]
    public void Type_Count_Is6()
    {
        var values = Enum.GetValues<CommandPaletteItemType>();
        Assert.AreEqual(6, values.Length);
    }

    // ── CommandPaletteGroup ─────────────────────────

    [TestMethod]
    public void Group_Constructor_PreservesItemsAndKey()
    {
        var items = new[]
        {
            new CommandPaletteItem { Title = "Copy" },
            new CommandPaletteItem { Title = "Cut" }
        };

        var group = new CommandPaletteGroup("Edit", items);

        Assert.AreEqual("Edit", group.Key);
        Assert.AreEqual(2, group.Count);
        Assert.AreEqual("Copy", group[0].Title);
        Assert.AreEqual("Cut", group[1].Title);
    }

    [TestMethod]
    public void Group_EmptyItems_Allowed()
    {
        var group = new CommandPaletteGroup("Empty", System.Array.Empty<CommandPaletteItem>());
        Assert.AreEqual("Empty", group.Key);
        Assert.AreEqual(0, group.Count);
    }
}
