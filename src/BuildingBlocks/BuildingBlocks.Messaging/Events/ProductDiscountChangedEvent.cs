namespace BuildingBlocks.Messaging.Events;

// Published by Pricing's CDC stream publisher whenever a product's campaign discount starts or
// stops applying — a product-discounts row appearing (CreateCampaign), being retracted
// (EndCampaign), or being dropped by DynamoDB TTL once the campaign's EndsAt passes (ADR-0044).
//
// Deliberately NOT a PriceChangedEvent: the nominal price did not change, and collapsing the two
// would make a campaign indistinguishable from a merchant repricing on the bus (ADR-0031 names an
// event after the occurrence, not after what a consumer happens to do with it).
//
// It carries the same recomputed payment highlights PriceChangedEvent does, because a consumer of
// either needs exactly the same fields to refresh its projection — the highlights are recomputed
// at publish time against the currently active GatewayCost and whatever discount is (or is no
// longer) in force, so a REMOVE naturally publishes the undiscounted figures.
public record ProductDiscountChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public decimal OriginalPrice { get; init; }
    public decimal Price { get; init; }
    public decimal CashPrice { get; init; }
    public int MaxInstallmentsWithoutInterest { get; init; }
    public decimal MaxInstallmentValue { get; init; }
}
