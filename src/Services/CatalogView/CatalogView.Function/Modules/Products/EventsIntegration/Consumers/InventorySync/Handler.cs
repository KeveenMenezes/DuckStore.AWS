using Microsoft.Extensions.Logging;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.InventorySync;

// Applies Inventory's storefront total to the product document (ADR-0052 §8). The AsOf condition makes
// delivery order irrelevant: a late or repeated event is dropped. A product whose ProductSynced has not
// arrived yet throws, so EventBridge retries until it has — the stock is never lost, and never written
// to a document without a name.
public sealed class InventorySyncHandler(IProductSearchIndex index, ILogger<InventorySyncHandler> logger)
{
    public async Task HandleAsync(ProductStockChangedEvent evt, CancellationToken cancellationToken = default)
    {
        var outcome = await index.ApplyStockAsync(evt.ProductId, evt.Available, evt.AsOf, cancellationToken);
        switch (outcome)
        {
            case StockApplyOutcome.ProductMissing:
                throw new InvalidOperationException(
                    $"Product {evt.ProductId} is not in catalogview-products yet; retrying its stock update.");
            case StockApplyOutcome.Stale:
                logger.LogInformation("Stock for {ProductId} as of {AsOf} is not newer than the stored one; dropped",
                    evt.ProductId, evt.AsOf);
                break;
        }
    }
}
