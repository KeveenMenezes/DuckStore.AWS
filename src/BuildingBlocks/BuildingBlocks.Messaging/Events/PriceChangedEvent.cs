namespace BuildingBlocks.Messaging.Events;

// Published by Pricing's CDC stream publisher whenever a product's nominal price is set or
// updated (prices table INSERT/MODIFY — ADR-0026). CatalogView consumes it and folds every field
// into the search document in one partial merge (ADR-0027/ADR-0028); ProductSyncedEvent
// deliberately carries none of this, since Pricing owns it.
//
// Price/CashPrice/MaxInstallmentsWithoutInterest are computed from the active GatewayCost provider
// (and any active campaign discount) at publish time — a single event carrying everything a price
// change affects, rather than one event per concern, so there is exactly one consumer merge per
// trigger. The full per-installment plan is deliberately NOT carried here: CatalogView only ever
// needs the scalar highlights for catalog/card display — the detailed plan (with real interest
// figures) is computed synchronously, on demand, by Pricing's GetInstallmentPlan query when the
// product detail page opens the payment-methods modal, so it never gets denormalized onto the
// search document.
public record PriceChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public decimal OriginalPrice { get; init; }
    public decimal Price { get; init; }
    public decimal CashPrice { get; init; }
    public int MaxInstallmentsWithoutInterest { get; init; }

    // The per-installment $ value at MaxInstallmentsWithoutInterest (Price itself when no count
    // clears the interest-free ceiling) — carried as a scalar so the catalog card can render an
    // exact "up to Nx of $Y" line without indexing the full per-installment plan (see
    // InstallmentCalculator.BuildInstallmentPlan).
    public decimal MaxInstallmentValue { get; init; }
}
