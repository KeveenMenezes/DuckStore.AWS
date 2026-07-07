using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.Prices.Data;

namespace Pricing.Function.Modules.Prices.EventsIntegration.Consumers.CatalogProductRemoved;

public static class CatalogProductRemovedHandler
{
    // Cleans up every row Pricing owns for a deleted product — no synchronous call to Catalog,
    // just a reaction to the CDC signal it already emits (see ADR-0026 §5).
    public static IReadOnlyList<TransactWriteItem> BuildCleanupTransactItems(Guid productId) =>
    [
        DynamoPriceRepository.ToDeleteTransactWriteItem(productId),
        DynamoCampaignRepository.ToDeleteProductDiscountTransactWriteItem(productId)
    ];
}
