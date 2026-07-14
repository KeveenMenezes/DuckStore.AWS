using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

namespace Catalog.UnitTests.EventsIntegration;

public class ProductDeletedRuleTests
{
    private readonly ProductDeletedRule _rule = new();

    private static CatalogStreamImage NewProduct(string id) =>
        new(id, "Debug Duck", "A duck", [], 10, ["cat-1"]);

    [Fact]
    public void Match_ShouldBeTrue_OnRemove()
    {
        var context = new StreamContext<CatalogStreamImage>("REMOVE", NewProduct("id-1"), null);

        Assert.True(_rule.Match(context));
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("MODIFY")]
    public void Match_ShouldBeFalse_OnNonRemove(string eventName)
    {
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>(eventName, product, product);

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_ShouldCarryProductIdFromOldImage()
    {
        var context = new StreamContext<CatalogStreamImage>("REMOVE", NewProduct("id-1"), null);

        var instruction = await _rule.BuildAsync(context);

        Assert.Equal(nameof(ProductDeletedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductDeletedEvent>(instruction.Payload);
        Assert.Equal("id-1", payload.ProductId);
    }
}
