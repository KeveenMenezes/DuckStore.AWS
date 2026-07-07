namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PriceChanged;

// Consumes PriceChangedEvent (ADR-0026 — Pricing owns the nominal price and, per ADR-0028, the
// payment badge computed alongside it) and merges every field into the product's OpenSearch
// document in one partial update. Setting absolute values is naturally idempotent, so no inbox
// table or event-id guard is needed (unlike ApplyRatingAsync, which increments).
public sealed class PriceSyncHandler(IProductSearchIndex index)
{
    public Task HandleAsync(PriceChangedEvent evt, CancellationToken cancellationToken = default) =>
        index.ApplyPricingAsync(
            evt.ProductId,
            evt.OriginalPrice,
            evt.Price,
            evt.CashPrice,
            evt.MaxInstallmentsWithoutInterest,
            evt.MaxInstallmentValue,
            cancellationToken);
}
