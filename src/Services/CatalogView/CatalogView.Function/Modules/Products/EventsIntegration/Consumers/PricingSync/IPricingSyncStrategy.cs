namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync;

// One business reason to write to catalogview-products on behalf of Pricing (ADR-0040). Pricing
// used to produce a single occurrence (PriceChangedEvent), so its consumer was allowed to stay a
// plain 1:1 handler; ADR-0044 added a second (ProductDiscountChangedEvent), which is the trigger
// ADR-0040's Future Constraints name for giving a producer its own strategy/dispatcher pair.
//
// Scoped to Pricing-sourced events only — Catalog and Review own their own interfaces — so a
// strategy from one producer can never be resolved by another producer's dispatcher.
public interface IPricingSyncStrategy
{
    bool CanHandle(string detailType);

    Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default);
}
