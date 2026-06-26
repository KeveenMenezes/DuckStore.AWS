namespace Ordering.Infrastructure.Data;

// Used by the BasketCheckoutEvent Lambda consumer for idempotency (inbox pattern) —
// unrelated to publishing OrderCreatedEvent, which now goes through DynamoDB Streams.
public class ProcessedIntegrationEvent
{
    public const string TableName = "ProcessedIntegrationEvents";

    public string MessageId { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}