using BuildingBlocks.Messaging.Events;
using Pricing.Function.Modules.Prices.EventsIntegration.Consumers.ProductDeleted;

namespace Pricing.UnitTests.Application.EventHandlers.Integration;

public class ProductDeletedMapperTests
{
    [Fact]
    public void ToProductId_ShouldParseProductIdFromEvent()
    {
        var productId = Guid.NewGuid();
        var evt = new ProductDeletedEvent { ProductId = productId.ToString() };

        var result = ProductDeletedMapper.ToProductId(evt);

        Assert.Equal(productId, result);
    }
}

public class ProductDeletedHandlerTests
{
    [Fact]
    public void BuildCleanupTransactItems_ShouldTargetPricesAndProductDiscountsTables()
    {
        var productId = Guid.NewGuid();

        var items = ProductDeletedHandler.BuildCleanupTransactItems(productId);

        Assert.Equal(2, items.Count);
        Assert.Equal("prices", items[0].Delete.TableName);
        Assert.Equal(productId.ToString(), items[0].Delete.Key["ProductId"].S);
        Assert.Equal("product-discounts", items[1].Delete.TableName);
        Assert.Equal(productId.ToString(), items[1].Delete.Key["ProductId"].S);
    }
}
