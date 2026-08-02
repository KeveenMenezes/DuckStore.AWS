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

    // PutEvents takes up to 10 entries per call, so a whole Streams batch usually publishes in one
    // request instead of one per record. Chunks are sent sequentially: EventBridge doesn't order
    // entries anyway, but a failing chunk must abort before the remaining ones are sent, so a
    // fail-fast retry replays the batch from a known point rather than from a hole in the middle.
    public async Task PublishManyAsync(
        IReadOnlyList<PublishInstruction> instructions, CancellationToken cancellationToken = default)
    {
        const int maxEntriesPerRequest = 10;

        for (var offset = 0; offset < instructions.Count; offset += maxEntriesPerRequest)
        {
            var chunk = instructions
                .Skip(offset)
                .Take(maxEntriesPerRequest)
                .Select(instruction => ToEntry(
                    instruction.DetailType,
                    Serialize(instruction.Payload, instruction.Payload.GetType())))
                .ToList();

            await SendAsync(chunk, DescribeDetailTypes(instructions, offset, chunk.Count), cancellationToken);
        }
    }

    // Names every detail-type in the chunk, so a failure log points at the actual events that were
    // in flight instead of just the first one.
    private static string DescribeDetailTypes(
        IReadOnlyList<PublishInstruction> instructions, int offset, int count) =>
        string.Join(", ", instructions.Skip(offset).Take(count).Select(i => i.DetailType).Distinct());

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

    public Task PublishRawAsync(
        string detailType, string detailJson, CancellationToken cancellationToken = default) =>
        SendAsync([ToEntry(detailType, detailJson)], detailType, cancellationToken);

    private PutEventsRequestEntry ToEntry(string detailType, string detailJson) =>
        new()
        {
            EventBusName = options.Value.BusName,
            Source = options.Value.Source,
            DetailType = detailType,
            Detail = detailJson
        };

    // `detailTypes` is only ever used to name what failed — one detail-type for a single publish,
    // a comma-separated list for a batch.
    private async Task SendAsync(
        List<PutEventsRequestEntry> entries, string detailTypes, CancellationToken cancellationToken)
    {
        var request = new PutEventsRequest { Entries = entries };

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
                    detailTypes, failure?.ErrorCode, failure?.ErrorMessage);

                if (FailFast)
                    throw new EventPublishException(detailTypes, failure?.ErrorCode, failure?.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not EventPublishException)
        {
            if (FailFast)
            {
                logger.LogError(ex, "Failed to publish {DetailType} to EventBridge.", detailTypes);
                throw;
            }

            logger.LogWarning(ex,
                "EventBridge unavailable while publishing {DetailType}; skipping (expected outside AWS).",
                detailTypes);
        }
    }
}
