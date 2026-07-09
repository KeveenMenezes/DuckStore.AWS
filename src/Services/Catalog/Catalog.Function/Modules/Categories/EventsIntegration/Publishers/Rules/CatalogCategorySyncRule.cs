namespace Catalog.Function.Modules.Categories.EventsIntegration.Publishers.Rules;

// Fires only on a rename (MODIFY where Name changed) — a brand-new category has no products
// referencing it yet, and a move never affects anything already denormalized on a product (ADR-0027
// extension). Produces CatalogCategorySyncEvent so CatalogView can rewrite the category's name on
// every product document that references it, without a synchronous call back into Catalog.
public sealed class CatalogCategorySyncRule : IStreamRule<CategoryStreamImage>
{
    public bool Match(StreamContext<CategoryStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.Old is not null
        && context.New is not null
        && context.Old.Name != context.New.Name;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CategoryStreamImage> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(CatalogCategorySyncEvent),
            new CatalogCategorySyncEvent
            {
                CategoryId = context.New!.Id,
                Name = context.New!.Name
            }));
}
