using BuildingBlocks.Messaging.Events;
using Pricing.Function.Modules.Prices.EventsIntegration.Consumers.CatalogProductRemoved;

namespace Pricing.UnitTests.Application.EventHandlers.Integration;

public class CatalogProductRemovedMapperTests
{
    [Fact]
    public void ToProductId_ShouldParseProductIdFromEvent()
    {
        var productId = Guid.NewGuid();
        var evt = new CatalogUpdatedEvent { ChangeType = "REMOVE", ProductId = productId.ToString() };

        var result = CatalogProductRemovedMapper.ToProductId(evt);

        Assert.Equal(productId, result);
    }
}

public class CatalogProductRemovedHandlerTests
{
    [Fact]
    public void BuildCleanupTransactItems_ShouldTargetPricesAndProductDiscountsTables()
    {
        var productId = Guid.NewGuid();

        var items = CatalogProductRemovedHandler.BuildCleanupTransactItems(productId);

        Assert.Equal(2, items.Count);
        Assert.Equal("prices", items[0].Delete.TableName);
        Assert.Equal(productId.ToString(), items[0].Delete.Key["ProductId"].S);
        Assert.Equal("product-discounts", items[1].Delete.TableName);
        Assert.Equal(productId.ToString(), items[1].Delete.Key["ProductId"].S);
    }
}
