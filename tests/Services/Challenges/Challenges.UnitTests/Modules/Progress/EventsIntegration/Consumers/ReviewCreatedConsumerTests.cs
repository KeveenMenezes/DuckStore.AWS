using BuildingBlocks.Messaging.Events;
using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.ReviewCreated;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Consumers;

public class ReviewCreatedConsumerTests
{
    private static readonly Guid ProductId = Guid.Parse("6755192f-0676-413c-8358-7d70c118fc2a");

    private static ReviewCreatedEvent Event(string userId = "6428f448-0001", Guid? productId = null) =>
        new()
        {
            ReviewId = $"{productId ?? ProductId}#{userId}",
            ProductId = productId ?? ProductId,
            UserId = userId,
            Rating = 5
        };

    [Fact]
    public void Mapper_ShouldPrefixTheCognitoSub_IntoAChallengesOwnerId()
    {
        var command = ReviewCreatedMapper.ToCommand(Event());

        Assert.Equal(OwnerId.Of("USER#6428f448-0001"), command.OwnerId);
        Assert.Equal(ProductId, command.ProductId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Mapper_ShouldReject_AnEventWithoutTheReviewer(string? userId)
    {
        // Thrown, not skipped: the invocation fails, EventBridge retries and the event lands in
        // the DLQ instead of a credit silently going to nobody.
        Assert.Throws<BuildingBlocks.Core.Exceptions.ValidationException>(
            () => ReviewCreatedMapper.ToCommand(Event(userId!)));
    }

    [Fact]
    public void Mapper_ShouldReject_AnEventWithoutTheProduct()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.ValidationException>(
            () => ReviewCreatedMapper.ToCommand(Event(productId: Guid.Empty)));
    }

    [Fact]
    public async Task Handler_ShouldCreditTheReviewPolicyAmount_ForThatProduct()
    {
        var repository = new Mock<IPlayerProgressRepository>();
        PointsTransaction? credited = null;
        repository
            .Setup(r => r.CreditAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PointsTransaction, CancellationToken>((t, _) => credited = t)
            .ReturnsAsync(true);

        var result = await new ReviewCreatedHandler(repository.Object)
            .HandleAsync(new CreditReviewCommand(OwnerId.Of("USER#alice"), ProductId));

        Assert.True(result);
        Assert.NotNull(credited);
        Assert.Equal($"REVIEW#{ProductId}", credited!.Id);
        Assert.Equal(PointsPolicy.ReviewCredit, credited.Points);
        Assert.Equal(PointsTransactionType.ReviewCredit, credited.Type);
        Assert.Equal(OwnerId.Of("USER#alice"), credited.OwnerId);
    }

    [Fact]
    public async Task Handler_ShouldBeANoOp_WhenTheProductWasAlreadyCredited()
    {
        var repository = new Mock<IPlayerProgressRepository>();
        repository
            .Setup(r => r.CreditAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await new ReviewCreatedHandler(repository.Object)
            .HandleAsync(new CreditReviewCommand(OwnerId.Of("USER#alice"), ProductId));

        Assert.False(result);
    }
}
