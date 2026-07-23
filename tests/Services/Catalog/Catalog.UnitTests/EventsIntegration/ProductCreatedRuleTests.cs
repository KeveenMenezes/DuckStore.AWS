using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

namespace Catalog.UnitTests.EventsIntegration;

public class ProductCreatedRuleTests
{
    private readonly ProductCreatedRule _rule = new();

    private static CatalogStreamImage NewProduct(string id) =>
        new(id, "Debug Duck", "A duck", [], 10, ["cat-1"]);

    [Fact]
    public void Match_ShouldBeTrue_OnInsert()
    {
        var context = new StreamContext<CatalogStreamImage>("INSERT", null, NewProduct("id-1"));

        Assert.True(_rule.Match(context));
    }

    [Theory]
    [InlineData("MODIFY")]
    [InlineData("REMOVE")]
    public void Match_ShouldBeFalse_OnNonInsert(string eventName)
    {
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>(eventName, product, product);

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_ShouldCarryProductId()
    {
        var context = new StreamContext<CatalogStreamImage>("INSERT", null, NewProduct("id-1"));

        var instruction = await _rule.BuildAsync(context);

        Assert.Equal(nameof(ProductCreatedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductCreatedEvent>(instruction.Payload);
        Assert.Equal("id-1", payload.ProductId);
    }
}
