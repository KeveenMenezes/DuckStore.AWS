using System.Text.Json;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync;

namespace CatalogView.UnitTests.Products;

public class ReviewSyncDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_RoutesToTheStrategyThatCanHandleTheDetailType()
    {
        var matching = new Mock<IReviewSyncStrategy>();
        matching.Setup(s => s.CanHandle(nameof(ReviewUpdatedEvent))).Returns(true);

        var other = new Mock<IReviewSyncStrategy>();
        other.Setup(s => s.CanHandle(It.IsAny<string>())).Returns(false);

        var dispatcher = new ReviewSyncDispatcher([other.Object, matching.Object]);

        var productId = Guid.NewGuid();
        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = nameof(ReviewUpdatedEvent),
            Detail = JsonSerializer.SerializeToElement(new ReviewUpdatedEvent
            {
                ReviewId = $"{productId}#dXNlcg==",
                ProductId = productId,
                OldRating = 3,
                NewRating = 5
            })
        };

        await dispatcher.DispatchAsync(evt);

        matching.Verify(
            s => s.HandleAsync("event-1", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        other.Verify(
            s => s.HandleAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenNoStrategyRegisteredForDetailType()
    {
        var dispatcher = new ReviewSyncDispatcher([]);

        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = "SomeUnknownEvent",
            Detail = JsonSerializer.SerializeToElement(new { })
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(evt));
    }
}
