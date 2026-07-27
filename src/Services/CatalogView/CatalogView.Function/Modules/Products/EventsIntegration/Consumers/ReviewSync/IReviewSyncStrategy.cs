namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync;

// One business reason to write to catalogview-products on behalf of Review (ADR-0040). A
// strategy answers "do I own this detail-type?" (CanHandle) and, when it does, applies its
// bounded write via IProductSearchIndex (HandleAsync). Scoped to Review-sourced events only —
// Catalog has its own ICatalogSyncStrategy — so a strategy from one producer can never be picked
// up by the other producer's dispatcher, even by mistake.
public interface IReviewSyncStrategy
{
    bool CanHandle(string detailType);

    Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default);
}
