using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using BuildingBlocks.Messaging.EventBridge;
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
