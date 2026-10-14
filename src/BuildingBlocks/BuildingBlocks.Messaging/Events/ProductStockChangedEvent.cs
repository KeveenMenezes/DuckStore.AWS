namespace BuildingBlocks.Messaging.Events;

// Published by Inventory's CDC stream publishers whenever a product's storefront stock may have
// changed: a balance moved, or a warehouse holding it was activated or deactivated (ADR-0052 §8).
// Available is the sum over the product's balances in active warehouses. Version is the sum of the
// write counters of those balance rows and their warehouses: every write that can change the total
// adds one, so a newer state always has a greater Version, and a consumer applies a total only when
// its Version is greater than the one it holds, whatever order events arrive in.
public record ProductStockChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public int Available { get; init; }
    public long Version { get; init; }
}
