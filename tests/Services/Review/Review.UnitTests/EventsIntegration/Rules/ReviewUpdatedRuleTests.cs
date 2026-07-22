using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

namespace Review.UnitTests.EventsIntegration.Rules;

public class ReviewUpdatedRuleTests
{
    private static ReviewStreamImage Review(Guid productId, int rating) =>
        new($"{productId}#user-id", productId, "user-id", "user", "comment", rating, "2026-01-01T00:00:00.000Z", "2026-01-02T00:00:00.000Z");

    [Fact]
    public void Match_ReturnsTrue_ForModify()
    {
        var rule = new ReviewUpdatedRule();
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>("MODIFY", Review(productId, 3), Review(productId, 5));

        Assert.True(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsTrue_ForModify_EvenWhenRatingUnchanged()
    {
        // A comment-only edit still fires — it also drives ISR revalidation, not just the rating.
        var rule = new ReviewUpdatedRule();
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>("MODIFY", Review(productId, 4), Review(productId, 4));

        Assert.True(rule.Match(context));
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonModify(string eventName)
    {
        var rule = new ReviewUpdatedRule();
        var review = Review(Guid.NewGuid(), 4);
        var context = new StreamContext<ReviewStreamImage>(eventName, review, review);

        Assert.False(rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsReviewUpdatedEvent_WithOldAndNewRating()
    {
        var rule = new ReviewUpdatedRule();
        var productId = Guid.NewGuid();
        var oldReview = Review(productId, 3);
        var newReview = Review(productId, 5);
        var context = new StreamContext<ReviewStreamImage>("MODIFY", oldReview, newReview);

        var instruction = await rule.BuildAsync(context);

        Assert.Equal(nameof(ReviewUpdatedEvent), instruction.DetailType);
        var evt = Assert.IsType<ReviewUpdatedEvent>(instruction.Payload);
        Assert.Equal(newReview.Id, evt.ReviewId);
        Assert.Equal(productId, evt.ProductId);
        Assert.Equal(3, evt.OldRating);
        Assert.Equal(5, evt.NewRating);
    }
}
