using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        // Best-effort: o bus EventBridge só existe na AWS. Sem ele (dev local) apenas
        // logamos um aviso em vez de quebrar o fluxo — a integração é testada na AWS.
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
