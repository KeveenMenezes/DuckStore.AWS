using BuildingBlocks.Messaging.Serialization;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync.Strategies;

// Consumes ProductDiscountChangedEvent — a campaign started, ended, or expired on this product
// (ADR-0044). The nominal price did not move; what changed is the discounted price the catalog
// card shows, which is why this is a separate occurrence from PriceChangedEvent even though the
// projection write is the same one.
//
// The event already carries the recomputed highlights (discounted on a start, undiscounted on an
// end or expiry), so this applies absolute values exactly like the price strategy and is likewise
// idempotent.
public sealed class ProductDiscountChangedStrategy(IProductSearchIndex index) : IPricingSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ProductDiscountChangedEvent);

    public Task HandleAsync(
        string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize(MessagingSerializerContext.Default.ProductDiscountChangedEvent)!;

        return index.ApplyPricingAsync(
            evt.ProductId,
            evt.OriginalPrice,
            evt.Price,
            evt.CashPrice,
            evt.MaxInstallmentsWithoutInterest,
            evt.MaxInstallmentValue,
            cancellationToken);
    }
}
