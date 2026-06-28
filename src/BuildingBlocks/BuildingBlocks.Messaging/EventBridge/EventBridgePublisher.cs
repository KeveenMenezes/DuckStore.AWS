using System.Text.Json;

namespace BuildingBlocks.Messaging.EventBridge;

public class EventBridgePublisher(
    IAmazonEventBridge client,
    IOptions<EventBridgeOptions> options,
    ILogger<EventBridgePublisher> logger)
    : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : notnull =>
        PublishRawAsync(typeof(T).Name, JsonSerializer.Serialize(message), cancellationToken);

    public async Task PublishRawAsync(
        string detailType, string detailJson, CancellationToken cancellationToken = default)
    {
        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = options.Value.BusName,
                    Source = options.Value.Source,
                    DetailType = detailType,
                    Detail = detailJson
                }
            ]
        };

        // Best-effort: the EventBridge bus only exists on AWS; locally we log a warning instead of breaking the flow.
        try
        {
            var response = await client.PutEventsAsync(request, cancellationToken);

            if (response.FailedEntryCount > 0)
            {
                var failure = response.Entries.Find(e => e.ErrorCode != null);
                logger.LogWarning(
                    "Failed to publish {DetailType} to EventBridge: {ErrorCode} - {ErrorMessage}",
                    detailType, failure?.ErrorCode, failure?.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "EventBridge unavailable while publishing {DetailType}; skipping (expected outside AWS).",
                detailType);
        }
    }
}
