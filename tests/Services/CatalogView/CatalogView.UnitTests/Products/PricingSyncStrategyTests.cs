using System.Text.Json;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class PriceChangedStrategyTests
{
    [Fact]
    public void CanHandle_OnlyPriceChangedEvent()
    {
        var strategy = new PriceChangedStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(PriceChangedEvent)));
        Assert.False(strategy.CanHandle(nameof(ProductDiscountChangedEvent)));
    }

    [Fact]
    public async Task HandleAsync_MergesPriceAndPaymentHighlightsIntoDocument_InOneCall()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new PriceChangedStrategy(index.Object);

        var evt = new PriceChangedEvent
        {
            ProductId = "product-1",
            OriginalPrice = 49.90m,
            Price = 45.00m,
            CashPrice = 42.75m,
            MaxInstallmentsWithoutInterest = 6,
            MaxInstallmentValue = 7.50m
        };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyPricingAsync(
                "product-1", 49.90m, 45.00m, 42.75m, 6, 7.50m, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class ProductDiscountChangedStrategyTests
{
    [Fact]
    public void CanHandle_OnlyProductDiscountChangedEvent()
    {
        var strategy = new ProductDiscountChangedStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ProductDiscountChangedEvent)));
        Assert.False(strategy.CanHandle(nameof(PriceChangedEvent)));
    }

    [Fact]
    public async Task HandleAsync_AppliesTheDiscountedHighlights_WhenACampaignStarts()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ProductDiscountChangedStrategy(index.Object);

        // OriginalPrice is the untouched sticker price; Price carries the campaign discount.
        var evt = new ProductDiscountChangedEvent
        {
            ProductId = "product-1",
            OriginalPrice = 49.90m,
            Price = 40.50m,
            CashPrice = 42.75m,
            MaxInstallmentsWithoutInterest = 6,
            MaxInstallmentValue = 6.75m
        };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyPricingAsync(
                "product-1", 49.90m, 40.50m, 42.75m, 6, 6.75m, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RollsThePriceBack_WhenTheCampaignEndedAndTheEventCarriesUndiscountedFigures()
    {
        // The publisher recomputes with no discount in force on a REMOVE, so rolling back is just
        // another absolute write — the strategy needs no notion of "ended".
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ProductDiscountChangedStrategy(index.Object);

        var evt = new ProductDiscountChangedEvent
        {
            ProductId = "product-1",
            OriginalPrice = 49.90m,
            Price = 45.00m,
            CashPrice = 42.75m,
            MaxInstallmentsWithoutInterest = 6,
            MaxInstallmentValue = 7.50m
        };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyPricingAsync(
                "product-1", 49.90m, 45.00m, 42.75m, 6, 7.50m, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class PricingSyncDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_RoutesToTheStrategyThatCanHandleTheDetailType()
    {
        var matching = new Mock<IPricingSyncStrategy>();
        matching.Setup(s => s.CanHandle(nameof(ProductDiscountChangedEvent))).Returns(true);

        var other = new Mock<IPricingSyncStrategy>();
        other.Setup(s => s.CanHandle(It.IsAny<string>())).Returns(false);

        var dispatcher = new PricingSyncDispatcher([other.Object, matching.Object]);

        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = nameof(ProductDiscountChangedEvent),
            Detail = JsonSerializer.SerializeToElement(
                new ProductDiscountChangedEvent { ProductId = "product-1" })
        };

        await dispatcher.DispatchAsync(evt);

        matching.Verify(
            s => s.HandleAsync("event-1", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        other.Verify(
            s => s.HandleAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenNoStrategyRegisteredForDetailType()
    {
        var dispatcher = new PricingSyncDispatcher([]);

        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = "SomeUnknownEvent",
            Detail = JsonSerializer.SerializeToElement(new { })
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(evt));
    }
}
