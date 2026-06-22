namespace BuildingBlocks.Messaging.EventBridge;

public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : notnull;

    Task PublishRawAsync(string detailType, string detailJson, CancellationToken cancellationToken = default);
}
