using Microsoft.Extensions.Logging;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.InventorySync;

// Applies Inventory's storefront total to the product document (ADR-0052 §8). The Version condition
// makes delivery order irrelevant: a late or repeated event is dropped. A total for a product not
// synced yet is kept on a stock-only item until its ProductSynced arrives.
public sealed class InventorySyncHandler(IProductSearchIndex index, ILogger<InventorySyncHandler> logger)
{
    public async Task HandleAsync(ProductStockChangedEvent evt, CancellationToken cancellationToken = default)
    {
        if (!await index.ApplyStockAsync(evt.ProductId, evt.Available, evt.Version, cancellationToken))
        {
            logger.LogInformation("Stock for {ProductId} at version {Version} is not newer than the stored one; dropped",
                evt.ProductId, evt.Version);
        }
    }
}
