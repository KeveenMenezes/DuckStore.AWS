namespace Ordering.Infrastructure.Data;

// Usado pelo Lambda consumer do BasketCheckoutEvent para idempotência (inbox pattern) —
// não tem relação com a publicação de OrderCreatedEvent, que agora é via DynamoDB Streams.
public class ProcessedIntegrationEvent
{
    public const string TableName = "ProcessedIntegrationEvents";

    public string MessageId { get; init; } = default!;
    public DateTime ProcessedOnUtc { get; init; }
}