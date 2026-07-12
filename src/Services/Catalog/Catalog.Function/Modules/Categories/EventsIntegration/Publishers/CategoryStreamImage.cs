namespace Catalog.Function.Modules.Categories.EventsIntegration.Publishers;

// The subset of a persisted category item the publisher rule reasons about, projected from a
// DynamoDB Streams image. Path is intentionally not included — nothing downstream needs it, only
// Name (see CatalogCategorySyncRule).
public sealed record CategoryStreamImage(string Id, string Name, string? ParentId)
{
    public static CategoryStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new CategoryStreamImage(
            image.TryGetValue("Id", out var id) ? id.S : string.Empty,
            image.TryGetValue("Name", out var name) ? name.S : string.Empty,
            image.TryGetValue("ParentId", out var parentId) ? parentId.S : null);
    }
}
