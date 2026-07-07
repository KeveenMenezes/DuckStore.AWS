using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;

namespace CatalogView.UnitTests.Products;

public class ReviewAggregateHandlerTests
{
    [Fact]
    public async Task HandleAsync_AppliesRating_WithEventIdForIdempotency()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new ReviewAggregateHandler(index.Object);

        var productId = Guid.NewGuid();
        var evt = new ReviewCreatedEvent { ReviewId = $"{productId}#dXNlcg==", ProductId = productId, Rating = 4 };
        const string eventId = "event-123";

        await handler.HandleAsync(eventId, evt);

        index.Verify(
            i => i.ApplyRatingAsync(productId.ToString(), eventId, 4, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
