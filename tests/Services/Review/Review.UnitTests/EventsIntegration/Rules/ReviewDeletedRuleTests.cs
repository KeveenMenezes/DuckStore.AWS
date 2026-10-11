using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

namespace Review.UnitTests.EventsIntegration.Rules;

// ADR-0049 §5: ReviewDeleted fires when a Published row is withdrawn; the TTL REMOVE is silent.
public class ReviewDeletedRuleTests
{
    private static ReviewStreamImage Review(Guid productId, int rating, string? status) =>
        new($"{productId}#user-id", productId, "user-id", "user", "comment", rating,
            "2026-01-01T00:00:00.000Z", "2026-01-02T00:00:00.000Z", status);

    [Theory]
    [InlineData("Published")]
    [InlineData(null)]
    public void Match_PublishedToDeleted_IsTrue_IncludingLegacyRows(string? oldStatus)
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 4, oldStatus), Review(productId, 4, "Deleted"));

        Assert.True(new ReviewDeletedRule().Match(context));
    }

    [Theory]
    [InlineData("Eligible", "Deleted")]
    [InlineData("Deleted", "Deleted")]
    [InlineData("Published", "Published")]
    [InlineData("Eligible", "Published")]
    [InlineData("Deleted", "Published")]
    public void Match_OtherTransitions_AreFalse(string oldStatus, string newStatus)
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 4, oldStatus), Review(productId, 4, newStatus));

        Assert.False(new ReviewDeletedRule().Match(context));
    }

    [Fact]
    public void Match_InsertEligible_IsFalse()
    {
        var context = new StreamContext<ReviewStreamImage>("INSERT", null, Review(Guid.NewGuid(), 0, "Eligible"));

        Assert.False(new ReviewDeletedRule().Match(context));
    }

    [Fact]
    public void Match_TtlRemove_IsFalse()
    {
        // The aggregate was already adjusted on delete; a second subtraction would double-count.
        var context = new StreamContext<ReviewStreamImage>("REMOVE", Review(Guid.NewGuid(), 4, "Deleted"), null);

        Assert.False(new ReviewDeletedRule().Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsReviewDeletedEvent_WithTheWithdrawnRatingFromTheOldImage()
    {
        var productId = Guid.NewGuid();
        var oldReview = Review(productId, 4, "Published");
        var context = new StreamContext<ReviewStreamImage>("MODIFY", oldReview, Review(productId, 0, "Deleted"));

        var instruction = await new ReviewDeletedRule().BuildAsync(context);

        Assert.Equal(nameof(ReviewDeletedEvent), instruction.DetailType);
        var evt = Assert.IsType<ReviewDeletedEvent>(instruction.Payload);
        Assert.Equal(oldReview.Id, evt.ReviewId);
        Assert.Equal(productId, evt.ProductId);
        Assert.Equal(4, evt.Rating);
    }
}
