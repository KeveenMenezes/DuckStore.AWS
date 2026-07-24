using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class ReviewUpdateStrategyTests
{
    [Fact]
    public void CanHandle_OnlyReviewUpdatedEvent()
    {
        var strategy = new ReviewUpdateStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ReviewUpdatedEvent)));
        Assert.False(strategy.CanHandle(nameof(ReviewCreatedEvent)));
    }

    [Fact]
    public async Task HandleAsync_AppliesRatingUpdate_WithOldAndNewRatingForEventIdempotency()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ReviewUpdateStrategy(index.Object);

        var productId = Guid.NewGuid();
        var evt = new ReviewUpdatedEvent
        {
            ReviewId = $"{productId}#dXNlcg==",
            ProductId = productId,
            OldRating = 3,
            NewRating = 5
        };
        const string eventId = "event-456";

        await strategy.HandleAsync(eventId, JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.ApplyRatingUpdateAsync(productId.ToString(), eventId, 3, 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
