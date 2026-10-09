namespace Challenges.UnitTests.Modules.Progress.Domain;

public class PointsTransactionTests
{
    private static readonly OwnerId Owner = OwnerId.Of("USER#alice");
    private static readonly DateTime At = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ChallengeCredit_ShouldBuildACompletedCredit_WithTheDeterministicTransactionId()
    {
        var credit = PointsTransaction.ChallengeCredit(Owner, QuestionId.Of("py-001"), 75, At);

        Assert.Equal("CHALLENGE#py-001", credit.Id);
        Assert.Equal(PointsTransactionsSchema.ChallengeTransactionId("py-001"), credit.Id);
        Assert.Equal(Owner, credit.OwnerId);
        Assert.Equal(PointsTransactionType.ChallengeCredit, credit.Type);
        Assert.Equal(PointsTransactionStatus.Completed, credit.Status);
        Assert.Equal(75, credit.Points);
        Assert.Equal("py-001", credit.SourceId);
        Assert.Equal(At, credit.CreatedAt);
        Assert.Equal(At, credit.UpdatedAt);
        Assert.Null(credit.OrderId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ChallengeCredit_ShouldReject_WhenPointsAreNotPositive(int points)
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(
            () => PointsTransaction.ChallengeCredit(Owner, QuestionId.Of("py-001"), points, At));
    }

    [Fact]
    public void Statuses_ShouldDeclareTheWholeRedemptionLifecycle_EvenThoughOnlyCreditsExistYet()
    {
        // ADR-0048 §5: the redemption states ship in the enum now so the persisted/GraphQL
        // vocabulary never has to change when cart-points-redemption lands.
        var names = Enum.GetNames<PointsTransactionStatus>();

        Assert.Equal(
            ["Completed", "Reserved", "Used", "Released", "Failed", "Refunded"],
            names);
    }
}
