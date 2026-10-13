namespace BuildingBlocks.Messaging.Events;

// Published by Inventory's CDC stream publishers whenever a product's storefront stock may have
// changed: a balance moved, or a warehouse holding it was activated or deactivated (ADR-0052 §8).
// Available is the sum over the product's balances in active warehouses. AsOf is the latest UpdatedAt
// among those balance rows and their warehouses — a function of the state, so a consumer applies a
// total only when its AsOf is newer than the one it holds, whatever order events arrive in.
public record ProductStockChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public int Available { get; init; }
    public string AsOf { get; init; } = string.Empty;
}
