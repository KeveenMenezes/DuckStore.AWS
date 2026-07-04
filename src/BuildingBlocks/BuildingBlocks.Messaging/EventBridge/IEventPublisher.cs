namespace BuildingBlocks.Messaging.EventBridge;

public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : notnull;

    // Publishes a rule-produced instruction: the only place that turns the generic (DetailType,
    // Payload) pair into an EventBridge event, so rules stay free of AWS types.
    Task PublishAsync(PublishInstruction instruction, CancellationToken cancellationToken = default);

    Task PublishRawAsync(string detailType, string detailJson, CancellationToken cancellationToken = default);
}
