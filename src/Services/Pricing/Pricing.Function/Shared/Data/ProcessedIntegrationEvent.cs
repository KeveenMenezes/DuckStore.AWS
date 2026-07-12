namespace Pricing.Function.Shared.Data;

// Used by the ProductDeleted Lambda consumer for idempotency (inbox pattern).
public class ProcessedIntegrationEvent
{
    public const string TableName = "pricing-processed-events";

    public string PK { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}
