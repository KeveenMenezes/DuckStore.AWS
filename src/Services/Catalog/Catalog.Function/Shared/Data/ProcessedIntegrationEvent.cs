namespace Catalog.Function.Shared.Data;

// Inbox table for the ReviewCreated Lambda consumer's idempotency (ADR-0011) — a redelivered
// EventBridge event is recorded once, so the rating counters are never double-counted.
public class ProcessedIntegrationEvent
{
    public const string TableName = "catalog-processed-events";

    public string PK { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}
