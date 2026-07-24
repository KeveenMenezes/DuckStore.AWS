namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync;

// One business reason to write to catalogview-products on behalf of Catalog (ADR-0040). A
// strategy answers "do I own this detail-type?" (CanHandle) and, when it does, applies its
// bounded write via IProductSearchIndex (HandleAsync). Scoped to Catalog-sourced events only —
// Review has its own IReviewSyncStrategy — so a strategy from one producer can never be picked
// up by the other producer's dispatcher, even by mistake.
public interface ICatalogSyncStrategy
{
    bool CanHandle(string detailType);

    Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default);
}
