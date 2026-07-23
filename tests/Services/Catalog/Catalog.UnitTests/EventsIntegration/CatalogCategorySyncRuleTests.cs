using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Catalog.Function.Modules.Categories.EventsIntegration.Publishers;
using Catalog.Function.Modules.Categories.EventsIntegration.Publishers.Rules;

namespace Catalog.UnitTests.EventsIntegration;

public class CatalogCategorySyncRuleTests
{
    private readonly CategorySyncRule _rule = new();

    [Fact]
    public void Match_ShouldBeTrue_WhenModifyChangesName()
    {
        var context = new StreamContext<CategoryStreamImage>(
            "MODIFY",
            new CategoryStreamImage("id-1", "Languages", null),
            new CategoryStreamImage("id-1", "Programming Languages", null));

        Assert.True(_rule.Match(context));
    }

    [Fact]
    public void Match_ShouldBeFalse_WhenModifyDoesNotChangeName()
    {
        var context = new StreamContext<CategoryStreamImage>(
            "MODIFY",
            new CategoryStreamImage("id-1", "Languages", null),
            new CategoryStreamImage("id-1", "Languages", "parent-1"));

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public void Match_ShouldBeFalse_OnInsert()
    {
        var context = new StreamContext<CategoryStreamImage>(
            "INSERT",
            null,
            new CategoryStreamImage("id-1", "Languages", null));

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public void Match_ShouldBeFalse_OnRemove()
    {
        var context = new StreamContext<CategoryStreamImage>(
            "REMOVE",
            new CategoryStreamImage("id-1", "Languages", null),
            null);

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_ShouldCarryNewNameAndId()
    {
        var context = new StreamContext<CategoryStreamImage>(
            "MODIFY",
            new CategoryStreamImage("id-1", "Languages", null),
            new CategoryStreamImage("id-1", "Programming Languages", null));

        var instruction = await _rule.BuildAsync(context);

        Assert.Equal(nameof(CatalogCategorySyncEvent), instruction.DetailType);
        var payload = Assert.IsType<CatalogCategorySyncEvent>(instruction.Payload);
        Assert.Equal("id-1", payload.CategoryId);
        Assert.Equal("Programming Languages", payload.Name);
    }
}
