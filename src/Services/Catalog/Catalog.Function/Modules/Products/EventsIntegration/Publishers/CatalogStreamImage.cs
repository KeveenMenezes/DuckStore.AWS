namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers;

// The subset of a persisted product item the publisher rules reason about, projected from a
// DynamoDB Streams image. Widened beyond just Id (ADR-0027) so CatalogSearchSyncRule can hydrate
// a complete CatalogProductSyncEvent without a callback into Catalog — CatalogProductChangedRule
// still only needs Id.
public sealed record CatalogStreamImage(
    string Id,
    string Name,
    string Description,
    string ImageUrl,
    int Stock,
    List<string> CategoryIds)
{
    public static CatalogStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new CatalogStreamImage(
            image.TryGetValue("Id", out var id) ? id.S : string.Empty,
            image.TryGetValue("Name", out var name) ? name.S : string.Empty,
            image.TryGetValue("Description", out var description) ? description.S : string.Empty,
            image.TryGetValue("ImageUrl", out var imageUrl) ? imageUrl.S : string.Empty,
            image.TryGetValue("Stock", out var stock) && !string.IsNullOrEmpty(stock.N)
                ? int.Parse(stock.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue("CategoryIds", out var categoryIds) && categoryIds.SS is not null
                ? [.. categoryIds.SS]
                : []);
    }
}
