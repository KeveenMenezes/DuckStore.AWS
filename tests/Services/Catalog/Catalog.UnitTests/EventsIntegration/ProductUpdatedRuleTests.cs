using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

namespace Catalog.UnitTests.EventsIntegration;

public class ProductUpdatedRuleTests
{
    private readonly ProductUpdatedRule _rule = new();

    private static CatalogStreamImage NewProduct(string id) =>
        new(id, "Debug Duck", "A duck", [], 10, ["cat-1"]);

    [Fact]
    public void Match_ShouldBeTrue_OnModify()
    {
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>("MODIFY", product, product);

        Assert.True(_rule.Match(context));
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("REMOVE")]
    public void Match_ShouldBeFalse_OnNonModify(string eventName)
    {
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>(eventName, product, product);

        Assert.False(_rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_ShouldCarryProductId()
    {
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>("MODIFY", product, product);

        var instruction = await _rule.BuildAsync(context);

        Assert.Equal(nameof(ProductUpdatedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductUpdatedEvent>(instruction.Payload);
        Assert.Equal("id-1", payload.ProductId);
    }
}
