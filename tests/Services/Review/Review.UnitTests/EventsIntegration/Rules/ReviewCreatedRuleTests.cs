using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

namespace Review.UnitTests.EventsIntegration.Rules;

// ADR-0049 §5: ReviewCreated fires when a row becomes publicly visible, not when it is inserted.
public class ReviewCreatedRuleTests
{
    private static ReviewStreamImage Review(Guid productId, int rating, string? status) =>
        new($"{productId}#user-id", productId, "user-id", "user", "comment", rating,
            "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", status);

    [Fact]
    public void Match_EligibleToPublished_IsTrue()
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 0, "Eligible"), Review(productId, 4, "Published"));

        Assert.True(new ReviewCreatedRule().Match(context));
    }

    [Fact]
    public void Match_DeletedToPublished_IsTrue()
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 3, "Deleted"), Review(productId, 5, "Published"));

        Assert.True(new ReviewCreatedRule().Match(context));
    }

    [Fact]
    public void Match_InsertEligible_IsFalse()
    {
        var context = new StreamContext<ReviewStreamImage>(
            "INSERT", null, Review(Guid.NewGuid(), 0, "Eligible"));

        Assert.False(new ReviewCreatedRule().Match(context));
    }

    [Fact]
    public void Match_InsertLegacyWithoutStatus_IsTrue()
    {
        var context = new StreamContext<ReviewStreamImage>("INSERT", null, Review(Guid.NewGuid(), 4, null));

        Assert.True(new ReviewCreatedRule().Match(context));
    }

    [Theory]
    [InlineData("Published", "Published")]
    [InlineData(null, "Published")]
    [InlineData(null, null)]
    [InlineData("Published", "Deleted")]
    [InlineData("Eligible", "Eligible")]
    [InlineData("Deleted", "Deleted")]
    public void Match_TransitionsThatAreNotANewPublication_AreFalse(string? oldStatus, string? newStatus)
    {
        var productId = Guid.NewGuid();
        var context = new StreamContext<ReviewStreamImage>(
            "MODIFY", Review(productId, 4, oldStatus), Review(productId, 4, newStatus));

        Assert.False(new ReviewCreatedRule().Match(context));
    }

    [Fact]
    public void Match_TtlRemove_IsFalse()
    {
        var context = new StreamContext<ReviewStreamImage>(
            "REMOVE", Review(Guid.NewGuid(), 4, "Deleted"), null);

        Assert.False(new ReviewCreatedRule().Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsReviewCreatedEvent_FromNewImage_WithUserId()
    {
        var productId = Guid.NewGuid();
        var review = Review(productId, 5, "Published");
        var context = new StreamContext<ReviewStreamImage>("MODIFY", Review(productId, 0, "Eligible"), review);

        var instruction = await new ReviewCreatedRule().BuildAsync(context);

        Assert.Equal(nameof(ReviewCreatedEvent), instruction.DetailType);
        var evt = Assert.IsType<ReviewCreatedEvent>(instruction.Payload);
        Assert.Equal(review.Id, evt.ReviewId);
        Assert.Equal(productId, evt.ProductId);
        Assert.Equal("user-id", evt.UserId);
        Assert.Equal(5, evt.Rating);
    }
}
