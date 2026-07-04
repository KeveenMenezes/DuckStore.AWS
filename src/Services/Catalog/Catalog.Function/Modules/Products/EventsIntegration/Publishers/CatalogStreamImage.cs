namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers;

// The subset of a persisted product item the publisher rules reason about, projected from a
// DynamoDB Streams image. Carrying Id is enough: any change to a product (rating update, edit,
// delete) is published as CatalogUpdatedEvent regardless of which attributes changed.
public sealed record CatalogStreamImage(string Id)
{
    public static CatalogStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new CatalogStreamImage(
            image.TryGetValue("Id", out var id) ? id.S : string.Empty);
    }
}
