using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

namespace Review.UnitTests.EventsIntegration.Rules;

public class ReviewCreatedRuleTests
{
    private static ReviewStreamImage NewReview(Guid productId, int rating) =>
        new($"{productId}#user-id", productId, "user-id", "user", "comment", rating, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z");

    [Fact]
    public void Match_ReturnsTrue_ForInsert()
    {
        var rule = new ReviewCreatedRule();
        var context = new StreamContext<ReviewStreamImage>("INSERT", null, NewReview(Guid.NewGuid(), 4));

        Assert.True(rule.Match(context));
    }

    [Theory]
    [InlineData("MODIFY")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonInsert(string eventName)
    {
        var rule = new ReviewCreatedRule();
        var review = NewReview(Guid.NewGuid(), 4);
        var context = new StreamContext<ReviewStreamImage>(eventName, review, review);

        Assert.False(rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsReviewCreatedEvent_FromNewImage()
    {
        var rule = new ReviewCreatedRule();
        var productId = Guid.NewGuid();
        var review = NewReview(productId, 5);
        var context = new StreamContext<ReviewStreamImage>("INSERT", null, review);

        var instruction = await rule.BuildAsync(context);

        Assert.Equal(nameof(ReviewCreatedEvent), instruction.DetailType);
        var evt = Assert.IsType<ReviewCreatedEvent>(instruction.Payload);
        Assert.Equal(review.Id, evt.ReviewId);
        Assert.Equal(productId, evt.ProductId);
        Assert.Equal(5, evt.Rating);
    }
}
