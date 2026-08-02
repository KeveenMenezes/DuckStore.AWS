namespace BuildingBlocks.Messaging.EventBridge;

public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : notnull;

    // Publishes a rule-produced instruction: the only place that turns the generic (DetailType,
    // Payload) pair into an EventBridge event, so rules stay free of AWS types.
    Task PublishAsync(PublishInstruction instruction, CancellationToken cancellationToken = default);

    // Batch sibling of the single-instruction overload, for the Streams-triggered publishers: one
    // Lambda invocation carries a whole batch of records (batchSize: 10 everywhere here), and
    // PutEvents accepts 10 entries per call — so the batch costs one API call, not one per record.
    //
    // Deliberately not another PublishAsync overload: passing a List<PublishInstruction> to one
    // would bind to PublishAsync<T> instead (identity conversion beats the interface conversion),
    // silently publishing the whole list as a single event of detail-type "List`1". A distinct
    // name makes that mistake unrepresentable.
    Task PublishManyAsync(
        IReadOnlyList<PublishInstruction> instructions, CancellationToken cancellationToken = default);

    Task PublishRawAsync(string detailType, string detailJson, CancellationToken cancellationToken = default);
}
