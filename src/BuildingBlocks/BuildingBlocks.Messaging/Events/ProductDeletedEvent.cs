namespace BuildingBlocks.Messaging.Events;

// Shared by every consumer that only needs to know a product was deleted (ADR-0031): Pricing
// deletes its own price/discount rows, CatalogView deletes the product from its search index.
// Neither needs the full product payload for a delete.
public record ProductDeletedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
}
