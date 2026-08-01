namespace BuildingBlocks.Messaging.Events;

// No currency field, ever — Challenges publishes what was spent, Pricing decides what it's worth
// (ADR-0046 §1, §3). Adding an amount here is NOT ALLOWED.
public record PointsRedeemedEvent : IntegrationEvent
{
    public string OwnerId { get; set; } = string.Empty;
    public string RedemptionId { get; set; } = string.Empty;
    public int Points { get; set; }
}
