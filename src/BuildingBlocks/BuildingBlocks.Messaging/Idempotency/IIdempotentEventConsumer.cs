namespace BuildingBlocks.Messaging.Idempotency;

public interface IIdempotentEventConsumer
{
    // Atomically registers eventId in the processed-events table and executes businessItems.
    // No-op if eventId was already registered (event already processed).
    Task ConsumeAsync(string eventId, IReadOnlyList<TransactWriteItem> businessItems,
        CancellationToken cancellationToken = default);
}
