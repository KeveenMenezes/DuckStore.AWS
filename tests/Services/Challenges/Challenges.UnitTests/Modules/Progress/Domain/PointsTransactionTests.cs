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

    private static readonly Guid Order = Guid.Parse("b91258ab-9298-43ae-9ee0-b65752590ec4");

    [Fact]
    public void Reserve_ShouldBuildAReservedDebit_KeyedAndIndexedByOrder()
    {
        var reservation = PointsTransaction.Reserve(Owner, Order, 1000, At);

        Assert.Equal($"REDEMPTION#{Order}", reservation.Id);
        Assert.Equal(PointsTransactionType.Redemption, reservation.Type);
        Assert.Equal(PointsTransactionStatus.Reserved, reservation.Status);
        Assert.Equal(-1000, reservation.Points); // signed: redemptions are negative
        Assert.Equal(Order.ToString(), reservation.SourceId);
        Assert.Equal(Order.ToString(), reservation.OrderId); // GSI1, for the settlement consumers
        Assert.Equal(At, reservation.CreatedAt);
    }

    [Fact]
    public void Fail_ShouldRecordThePointsAsked_WithoutBeingADebit()
    {
        var failed = PointsTransaction.Fail(Owner, Order, 1000, At);

        Assert.Equal($"REDEMPTION#{Order}", failed.Id);
        Assert.Equal(PointsTransactionStatus.Failed, failed.Status);
        Assert.Equal(-1000, failed.Points);
        Assert.Equal(Order.ToString(), failed.OrderId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Reserve_ShouldReject_NonPositivePoints(int points)
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(
            () => PointsTransaction.Reserve(Owner, Order, points, At));
    }

    [Fact]
    public void Reserve_ShouldReject_AnEmptyOrderId()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(
            () => PointsTransaction.Reserve(Owner, Guid.Empty, 100, At));
    }

    private static readonly DateTime Later = At.AddMinutes(5);

    private static PointsTransaction Redemption(PointsTransactionStatus status) =>
        PointsTransaction.Load(
            Owner, $"REDEMPTION#{Order}", PointsTransactionType.Redemption, status, -1000,
            Order.ToString(), Order.ToString(), At, At);

    [Fact]
    public void Use_ShouldSettleAReservation_WithoutReturningPoints()
    {
        var redemption = Redemption(PointsTransactionStatus.Reserved);

        Assert.True(redemption.Use(Later));
        Assert.Equal(PointsTransactionStatus.Used, redemption.Status);
        Assert.Equal(PointsTransactionStatus.Reserved, redemption.PreviousStatus);
        Assert.Equal(0, redemption.PointsToReturn);
        Assert.Equal(Later, redemption.UpdatedAt);
    }

    [Theory]
    [InlineData(PointsTransactionStatus.Used)]
    [InlineData(PointsTransactionStatus.Failed)]
    [InlineData(PointsTransactionStatus.Released)]
    public void Use_ShouldBeANoOp_UnlessReserved(PointsTransactionStatus status)
    {
        var redemption = Redemption(status);

        Assert.False(redemption.Use(Later));
        Assert.Equal(status, redemption.Status);
    }

    [Fact]
    public void CancelOrder_ShouldReleaseAReservation_AndGiveThePointsBack()
    {
        var redemption = Redemption(PointsTransactionStatus.Reserved);

        Assert.True(redemption.CancelOrder(Later));
        Assert.Equal(PointsTransactionStatus.Released, redemption.Status);
        Assert.Equal(1000, redemption.PointsToReturn); // exactly what left, read from the row
    }

    [Fact]
    public void CancelOrder_ShouldRefundAUsedRedemption_AndGiveThePointsBack()
    {
        var redemption = Redemption(PointsTransactionStatus.Used);

        Assert.True(redemption.CancelOrder(Later));
        Assert.Equal(PointsTransactionStatus.Refunded, redemption.Status);
        Assert.Equal(PointsTransactionStatus.Used, redemption.PreviousStatus);
        Assert.Equal(1000, redemption.PointsToReturn);
    }

    // Failed never debited anything; Released/Refunded already gave the points back.
    [Theory]
    [InlineData(PointsTransactionStatus.Failed)]
    [InlineData(PointsTransactionStatus.Released)]
    [InlineData(PointsTransactionStatus.Refunded)]
    public void CancelOrder_ShouldBeANoOp_WhenThereIsNothingToGiveBack(PointsTransactionStatus status)
    {
        var redemption = Redemption(status);

        Assert.False(redemption.CancelOrder(Later));
        Assert.Equal(status, redemption.Status);
        Assert.Equal(0, redemption.PointsToReturn);
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
