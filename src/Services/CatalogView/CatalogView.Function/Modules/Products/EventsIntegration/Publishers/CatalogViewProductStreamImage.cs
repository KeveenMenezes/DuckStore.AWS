namespace CatalogView.Function.Modules.Products.EventsIntegration.Publishers;

// The subset of a catalogview-products item the publisher rules reason about, projected from a
// DynamoDB Streams image. The table has no Type discriminator (single-entity, Id == ProductId) —
// the id is all either rule needs.
public sealed record CatalogViewProductStreamImage(string Id)
{
    public static CatalogViewProductStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return image.TryGetValue("Id", out var id) && !string.IsNullOrEmpty(id.S)
            ? new CatalogViewProductStreamImage(id.S)
            : null;
    }
}
