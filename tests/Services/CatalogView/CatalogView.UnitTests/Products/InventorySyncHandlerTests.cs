using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.InventorySync;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatalogView.UnitTests.Products;

public class InventorySyncHandlerTests
{
    private readonly Mock<IProductSearchIndex> _index = new();
    private static readonly ProductStockChangedEvent Event =
        new() { ProductId = "product-1", Available = 70, AsOf = "2026-10-13T12:00:00.000Z" };

    private InventorySyncHandler Handler => new(_index.Object, NullLogger<InventorySyncHandler>.Instance);

    private void Outcome(StockApplyOutcome outcome) =>
        _index.Setup(i => i.ApplyStockAsync("product-1", 70, "2026-10-13T12:00:00.000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    [Fact]
    public async Task ATotal_IsAppliedWithItsAsOf()
    {
        Outcome(StockApplyOutcome.Applied);

        await Handler.HandleAsync(Event);

        _index.VerifyAll();
    }

    [Fact]
    public async Task ALateOrRepeatedEvent_IsDroppedWithoutError()
    {
        Outcome(StockApplyOutcome.Stale);

        await Handler.HandleAsync(Event);
    }

    [Fact]
    public async Task AProductNotSyncedYet_Throws_SoEventBridgeRetries()
    {
        Outcome(StockApplyOutcome.ProductMissing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler.HandleAsync(Event));
    }
}
