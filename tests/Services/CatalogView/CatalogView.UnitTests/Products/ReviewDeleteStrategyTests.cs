using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class ReviewDeleteStrategyTests
{
    [Fact]
    public void CanHandle_OnlyReviewDeletedEvent()
    {
        var strategy = new ReviewDeleteStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ReviewDeletedEvent)));
        Assert.False(strategy.CanHandle(nameof(ReviewCreatedEvent)));
        Assert.False(strategy.CanHandle(nameof(ReviewUpdatedEvent)));
    }

    [Fact]
    public async Task HandleAsync_RemovesTheWithdrawnRating_WithEventIdForIdempotency()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ReviewDeleteStrategy(index.Object);

        var productId = Guid.NewGuid();
        var evt = new ReviewDeletedEvent
        {
            ReviewId = $"{productId}#user-id",
            ProductId = productId,
            Rating = 4
        };
        const string eventId = "event-789";

        await strategy.HandleAsync(eventId, JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyRatingRemovalAsync(productId.ToString(), eventId, 4, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
