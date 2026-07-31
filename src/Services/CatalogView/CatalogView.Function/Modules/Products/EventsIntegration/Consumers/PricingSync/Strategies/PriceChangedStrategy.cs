using BuildingBlocks.Messaging.Serialization;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync.Strategies;

// Consumes PriceChangedEvent — the merchant set or changed the nominal price (ADR-0026), and
// Pricing recomputed the payment badge alongside it (ADR-0028). Setting absolute values is
// naturally idempotent, so no inbox table or event-id guard is needed (unlike ApplyRatingAsync,
// which increments).
public sealed class PriceChangedStrategy(IProductSearchIndex index) : IPricingSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(PriceChangedEvent);

    public Task HandleAsync(
        string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize(MessagingSerializerContext.Default.PriceChangedEvent)!;

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
