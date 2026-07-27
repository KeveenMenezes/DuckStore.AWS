using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class ReviewCreateStrategyTests
{
    [Fact]
    public void CanHandle_OnlyReviewCreatedEvent()
    {
        var strategy = new ReviewCreateStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ReviewCreatedEvent)));
        Assert.False(strategy.CanHandle(nameof(ReviewUpdatedEvent)));
    }

    [Fact]
    public async Task HandleAsync_AppliesRating_WithEventIdForIdempotency()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ReviewCreateStrategy(index.Object);

        var productId = Guid.NewGuid();
        var evt = new ReviewCreatedEvent { ReviewId = $"{productId}#dXNlcg==", ProductId = productId, Rating = 4 };
        const string eventId = "event-123";

        await strategy.HandleAsync(eventId, JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyRatingAsync(
                productId.ToString(),
                eventId, 4,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
