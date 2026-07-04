namespace BuildingBlocks.Messaging.Events;

public record CatalogUpdatedEvent : IntegrationEvent
{
    public string ChangeType { get; init; } = string.Empty;

    // The changed product's Id (from the DynamoDB Streams record's key) — lets
    // consumers invalidate a per-product cache tag (e.g. products:{id}) instead
    // of only the blanket "products" tag.
    public string ProductId { get; init; } = string.Empty;
}
