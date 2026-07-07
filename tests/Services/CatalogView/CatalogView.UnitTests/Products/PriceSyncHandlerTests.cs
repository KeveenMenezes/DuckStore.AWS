using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PriceChanged;

namespace CatalogView.UnitTests.Products;

public class PriceSyncHandlerTests
{
    [Fact]
    public async Task HandleAsync_MergesPriceAndPaymentHighlightsIntoDocument_InOneCall()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new PriceSyncHandler(index.Object);

        var productId = Guid.NewGuid().ToString();
        var evt = new PriceChangedEvent
        {
            ProductId = productId,
            OriginalPrice = 49.90m,
            Price = 45.00m,
            CashPrice = 42.75m,
            MaxInstallmentsWithoutInterest = 6,
            MaxInstallmentValue = 7.50m
        };

        await handler.HandleAsync(evt);

        index.Verify(
            i => i.ApplyPricingAsync(
                productId,
                49.90m,
                45.00m,
                42.75m,
                6,
                7.50m,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
