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

    [Fact]
    public void ChallengeCredit_ShouldStoreTimestampsInUtc_WhenGivenALocalTime()
    {
        // CreatedAt is the LSI1 sort key, persisted as ISO-8601 and read back by
        // Query.myPointsHistory as UTC — a local time would serialize with an offset instead.
        var local = At.ToLocalTime();

        var credit = PointsTransaction.ChallengeCredit(Owner, QuestionId.Of("py-001"), 75, local);

        Assert.Equal(DateTimeKind.Utc, credit.CreatedAt!.Value.Kind);
        Assert.Equal(At, credit.CreatedAt);
        Assert.Equal(At, credit.UpdatedAt);
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
    public void ReviewCredit_ShouldBuildACompletedCreditOfThePolicyAmount_KeyedByProduct()
    {
        var productId = Guid.Parse("6755192f-0676-413c-8358-7d70c118fc2a");

        var credit = PointsTransaction.ReviewCredit(Owner, productId, At.ToLocalTime());

        // REVIEW#<productId> is the "once per product" lock (SPEC-review-points): it holds even
        // after the review is deleted, expires by TTL and is published again.
        Assert.Equal($"REVIEW#{productId}", credit.Id);
        Assert.Equal(Owner, credit.OwnerId);
        Assert.Equal(PointsTransactionType.ReviewCredit, credit.Type);
        Assert.Equal(PointsTransactionStatus.Completed, credit.Status);
        Assert.Equal(15, credit.Points);
        Assert.Equal(PointsPolicy.ReviewCredit, credit.Points);
        Assert.Equal(productId.ToString(), credit.SourceId);
        Assert.Equal(At, credit.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, credit.CreatedAt!.Value.Kind);
        Assert.Null(credit.OrderId);
    }

    [Fact]
    public void ReviewCredit_ShouldReject_AnEmptyProductId()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(
            () => PointsTransaction.ReviewCredit(Owner, Guid.Empty, At));
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
