namespace Payment.Function.Shared.Data;

// Used by the OrderCreated/PaymentResult Lambda consumers for idempotency (inbox pattern) —
// unrelated to publishing PaymentRequestedEvent, which goes through DynamoDB Streams.
public class ProcessedIntegrationEvent
{
    public const string TableName = "payment-processed-events";

    public string PK { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}
