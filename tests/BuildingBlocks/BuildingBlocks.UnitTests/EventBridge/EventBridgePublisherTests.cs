using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.UnitTests.EventBridge;

public class EventBridgePublisherTests
{
    private readonly Mock<IAmazonEventBridge> _client = new();
    private readonly Mock<ILogger<EventBridgePublisher>> _logger = new();
    private readonly EventBridgeOptions _options = new();

    private EventBridgePublisher CreatePublisher() =>
        new(_client.Object, Options.Create(_options), _logger.Object);

    [Fact]
    public async Task PublishRawAsync_BestEffort_LogsWarning_WhenPutEventsThrows()
    {
        _options.FailFast = false;
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonEventBridgeException("boom"));

        var publisher = CreatePublisher();

        await publisher.PublishRawAsync("SomeEvent", "{}");

        _logger.VerifyLogWithException(LogLevel.Warning, "SomeEvent");
    }

    [Fact]
    public async Task PublishRawAsync_BestEffort_LogsError_WhenFailedEntryCountGreaterThanZero()
    {
        _options.FailFast = false;
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedResponse());

        var publisher = CreatePublisher();

        await publisher.PublishRawAsync("SomeEvent", "{}");

        _logger.VerifyLog(LogLevel.Error, "SomeEvent");
    }

    [Fact]
    public async Task PublishRawAsync_FailFast_Throws_WhenPutEventsThrows()
    {
        _options.FailFast = true;
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonEventBridgeException("boom"));

        var publisher = CreatePublisher();

        await Assert.ThrowsAsync<AmazonEventBridgeException>(() =>
            publisher.PublishRawAsync("SomeEvent", "{}"));
    }

    [Fact]
    public async Task PublishRawAsync_FailFast_ThrowsEventPublishException_WhenFailedEntryCountGreaterThanZero()
    {
        _options.FailFast = true;
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedResponse());

        var publisher = CreatePublisher();

        await Assert.ThrowsAsync<EventPublishException>(() =>
            publisher.PublishRawAsync("SomeEvent", "{}"));
    }

    // The reason PublishManyAsync exists: a Streams batch becomes one API call, not one per event.
    [Fact]
    public async Task PublishManyAsync_SendsEveryInstruction_InASinglePutEvents_WhenTheBatchFits()
    {
        var requests = CaptureRequests();

        await CreatePublisher().PublishManyAsync(Instructions(count: 7));

        var request = Assert.Single(requests);
        Assert.Equal(7, request.Entries.Count);
        Assert.All(request.Entries, entry => Assert.Equal(nameof(ProductUpdatedEvent), entry.DetailType));
    }

    // PutEvents rejects more than 10 entries per call, so a longer batch has to be split.
    [Fact]
    public async Task PublishManyAsync_SplitsIntoChunksOfTen_WhenTheBatchExceedsThePutEventsLimit()
    {
        var requests = CaptureRequests();

        await CreatePublisher().PublishManyAsync(Instructions(count: 23));

        Assert.Equal([10, 10, 3], requests.Select(r => r.Entries.Count));
    }

    [Fact]
    public async Task PublishManyAsync_SendsNothing_WhenThereAreNoInstructions()
    {
        var requests = CaptureRequests();

        await CreatePublisher().PublishManyAsync([]);

        Assert.Empty(requests);
    }

    // A failing chunk must abort the rest: a fail-fast retry then replays the batch from a known
    // point instead of from a hole in the middle of it.
    [Fact]
    public async Task PublishManyAsync_StopsAtTheFailingChunk_WhenFailFastIsOn()
    {
        _options.FailFast = true;
        var requests = new List<PutEventsRequest>();
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutEventsRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(FailedResponse());

        await Assert.ThrowsAsync<EventPublishException>(() =>
            CreatePublisher().PublishManyAsync(Instructions(count: 23)));

        Assert.Single(requests);
    }

    private List<PutEventsRequest> CaptureRequests()
    {
        var requests = new List<PutEventsRequest>();
        _client
            .Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutEventsRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new PutEventsResponse { FailedEntryCount = 0, Entries = [] });
        return requests;
    }

    // Any event registered in MessagingSerializerContext works here; the payload's shape is
    // irrelevant to batching, only that it has a compile-time JSON contract.
    private static List<PublishInstruction> Instructions(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PublishInstruction(
            nameof(ProductUpdatedEvent),
            new ProductUpdatedEvent { ProductId = i.ToString() }))];

    private static PutEventsResponse FailedResponse() =>
        new()
        {
            FailedEntryCount = 1,
            Entries =
            [
                new PutEventsResultEntry { ErrorCode = "InternalFailure", ErrorMessage = "nope" }
            ]
        };
}
