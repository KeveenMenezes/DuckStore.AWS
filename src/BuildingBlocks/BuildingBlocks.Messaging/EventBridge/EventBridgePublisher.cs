using System.Text.Json;
using BuildingBlocks.Messaging.Serialization;

namespace BuildingBlocks.Messaging.EventBridge;

public class EventBridgePublisher(
    IAmazonEventBridge client,
    IOptions<EventBridgeOptions> options,
    ILogger<EventBridgePublisher> logger)
    : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : notnull =>
        PublishRawAsync(typeof(T).Name, Serialize(message, typeof(T)), cancellationToken);

    public Task PublishAsync(PublishInstruction instruction, CancellationToken cancellationToken = default) =>
        PublishRawAsync(
            instruction.DetailType,
            Serialize(instruction.Payload, instruction.Payload.GetType()),
            cancellationToken);

    // Source-generated contracts only: under Native AOT there is no reflection fallback, so an
    // unregistered event would fail at invocation. Failing here names the type instead.
    private static string Serialize(object value, Type type)
    {
        var typeInfo = MessagingSerializerContext.Default.GetTypeInfo(type)
            ?? throw new InvalidOperationException(
                $"No JSON contract for {type.Name}. Add [JsonSerializable(typeof({type.Name}))] "
                + $"to {nameof(MessagingSerializerContext)}.");

        return JsonSerializer.Serialize(value, typeInfo);
    }

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
