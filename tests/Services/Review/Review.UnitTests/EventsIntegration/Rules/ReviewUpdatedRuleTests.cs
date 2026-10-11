using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

namespace Review.UnitTests.EventsIntegration.Rules;

// ADR-0049 §5: ReviewUpdated fires only on an edit of a row that stays Published.
public class ReviewUpdatedRuleTests
{
    private static ReviewStreamImage Review(Guid productId, int rating, string? status) =>
        new($"{productId}#user-id", productId, "user-id", "user", "comment", rating,
            "2026-01-01T00:00:00.000Z", "2026-01-02T00:00:00.000Z", status);

    [Theory]
    [InlineData("Published", "Published")]
    [InlineData(null, "Published")]
    [InlineData(null, null)]
    public void Match_PublishedToPublished_IsTrue_IncludingLegacyRows(string? oldStatus, string? newStatus)
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 3, oldStatus), Review(productId, 5, newStatus));

        Assert.True(new ReviewUpdatedRule().Match(context));
    }

    [Fact]
    public void Match_PublishedToPublished_EvenWhenRatingUnchanged_IsTrue()
    {
        // A comment-only edit still fires — it also drives ISR revalidation, not just the rating.
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 4, "Published"), Review(productId, 4, "Published"));

        Assert.True(new ReviewUpdatedRule().Match(context));
    }

    [Theory]
    [InlineData("Eligible", "Published")]
    [InlineData("Deleted", "Published")]
    [InlineData("Published", "Deleted")]
    [InlineData("Eligible", "Eligible")]
    [InlineData("Deleted", "Deleted")]
    public void Match_TransitionsOutsidePublished_AreFalse(string oldStatus, string newStatus)
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 4, oldStatus), Review(productId, 4, newStatus));

        Assert.False(new ReviewUpdatedRule().Match(context));
    }

    [Fact]
    public void Match_InsertEligible_IsFalse()
    {
        var context = new StreamContext<ReviewStreamImage>("INSERT", null, Review(Guid.NewGuid(), 0, "Eligible"));

        Assert.False(new ReviewUpdatedRule().Match(context));
    }

    [Fact]
    public void Match_TtlRemove_IsFalse()
    {
        var context = new StreamContext<ReviewStreamImage>("REMOVE", Review(Guid.NewGuid(), 4, "Deleted"), null);

        Assert.False(new ReviewUpdatedRule().Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsReviewUpdatedEvent_WithOldAndNewRating()
    {
        var productId = Guid.NewGuid();
        var oldReview = Review(productId, 3, "Published");
        var newReview = Review(productId, 5, "Published");
        var context = new StreamContext<ReviewStreamImage>("MODIFY", oldReview, newReview);

        var instruction = await new ReviewUpdatedRule().BuildAsync(context);

        Assert.Equal(nameof(ReviewUpdatedEvent), instruction.DetailType);
        var evt = Assert.IsType<ReviewUpdatedEvent>(instruction.Payload);
        Assert.Equal(newReview.Id, evt.ReviewId);
        Assert.Equal(productId, evt.ProductId);
        Assert.Equal(3, evt.OldRating);
        Assert.Equal(5, evt.NewRating);
    }
}
