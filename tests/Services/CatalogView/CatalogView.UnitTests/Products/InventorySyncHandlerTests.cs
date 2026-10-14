using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.InventorySync;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatalogView.UnitTests.Products;

public class InventorySyncHandlerTests
{
    private readonly Mock<IProductSearchIndex> _index = new();
    private static readonly ProductStockChangedEvent Event = new() { ProductId = "product-1", Available = 70, Version = 12 };

    private InventorySyncHandler Handler => new(_index.Object, NullLogger<InventorySyncHandler>.Instance);

    private void Applied(bool applied) =>
        _index.Setup(i => i.ApplyStockAsync("product-1", 70, 12, It.IsAny<CancellationToken>())).ReturnsAsync(applied);

    [Fact]
    public async Task ATotal_IsAppliedWithItsVersion()
    {
        Applied(true);

        await Handler.HandleAsync(Event);

        _index.VerifyAll();
    }

    // Retrying would never help: the stored total is already newer.
    [Fact]
    public async Task ALateOrRepeatedEvent_IsDroppedWithoutError()
    {
        Applied(false);

        await Handler.HandleAsync(Event);

        _index.VerifyAll();
    }
}
