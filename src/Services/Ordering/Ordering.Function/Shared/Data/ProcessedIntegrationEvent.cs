namespace Ordering.Function.Shared.Data;

// Used by the BasketCheckoutEvent Lambda consumer for idempotency (inbox pattern) —
// unrelated to publishing OrderCreatedEvent, which now goes through DynamoDB Streams.
public class ProcessedIntegrationEvent
{
    public const string TableName = "ordering-processed-events";

    public string PK { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}
