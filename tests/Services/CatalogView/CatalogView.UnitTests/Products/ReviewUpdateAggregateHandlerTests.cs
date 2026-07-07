using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewUpdated;

namespace CatalogView.UnitTests.Products;

public class ReviewUpdateAggregateHandlerTests
{
    [Fact]
    public async Task HandleAsync_AppliesRatingUpdate_WithOldAndNewRatingForEventIdempotency()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new ReviewUpdateAggregateHandler(index.Object);

        var productId = Guid.NewGuid();
        var evt = new ReviewUpdatedEvent
        {
            ReviewId = $"{productId}#dXNlcg==",
            ProductId = productId,
            OldRating = 3,
            NewRating = 5
        };
        const string eventId = "event-456";

        await handler.HandleAsync(eventId, evt);

        index.Verify(
            i => i.ApplyRatingUpdateAsync(productId.ToString(), eventId, 3, 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
