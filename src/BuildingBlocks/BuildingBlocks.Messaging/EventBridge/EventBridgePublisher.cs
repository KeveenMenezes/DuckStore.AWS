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

    public Task PublishAsync(PublishInstruction instruction, CancellationToken cancellationToken = default) =>
        PublishRawAsync(instruction.DetailType, JsonSerializer.Serialize(instruction.Payload), cancellationToken);

    // Fail fast when running inside a real Lambda runtime; best-effort everywhere else.
    // EventBridge__FailFast (bool) overrides the detection in either direction when set.
    private bool FailFast =>
        options.Value.FailFast
        ?? Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") is not null;

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

        // On AWS, a swallowed failure would let the DynamoDB Streams checkpoint advance past a lost
        // event; locally the bus doesn't exist, so we keep logging and continuing (see ADR-0021).
        try
        {
            var response = await client.PutEventsAsync(request, cancellationToken);

            if (response.FailedEntryCount > 0)
            {
                var failure = response.Entries.Find(e => e.ErrorCode != null);
                logger.LogError(
                    "Failed to publish {DetailType} to EventBridge: {ErrorCode} - {ErrorMessage}",
                    detailType, failure?.ErrorCode, failure?.ErrorMessage);

                if (FailFast)
                    throw new EventPublishException(detailType, failure?.ErrorCode, failure?.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not EventPublishException)
        {
            if (FailFast)
            {
                logger.LogError(ex, "Failed to publish {DetailType} to EventBridge.", detailType);
                throw;
            }

            logger.LogWarning(ex,
                "EventBridge unavailable while publishing {DetailType}; skipping (expected outside AWS).",
                detailType);
        }
    }
}
