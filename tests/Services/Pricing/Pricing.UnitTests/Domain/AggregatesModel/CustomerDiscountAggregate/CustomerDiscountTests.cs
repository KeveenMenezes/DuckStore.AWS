namespace Pricing.UnitTests.Domain.AggregatesModel.CustomerDiscountAggregate;

public class CustomerDiscountTests
{
    private static RewardOptions Rewards() => new()
    {
        PointsPerUnit = 100,
        CurrencyPerUnit = 10m,
        ExpiryDays = 90
    };

    [Fact]
    public void Issue_ShouldConvertPointsToCurrency_UsingTheGivenRewardOptions()
    {
        var discount = CustomerDiscount.Issue(
            "USER#alice", points: 500, Rewards(), "redemption-1", DateTime.UtcNow);

        Assert.Equal(50m, discount.Amount);
    }

    [Fact]
    public void Issue_ShouldSetExpiresAt_ToIssuedAtPlusExpiryDays()
    {
        var issuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var discount = CustomerDiscount.Issue("USER#alice", 500, Rewards(), "redemption-1", issuedAt);

        Assert.Equal(issuedAt.AddDays(90), discount.ExpiresAt);
    }

    [Fact]
    public void Issue_ShouldStartAsIssued()
    {
        var discount = CustomerDiscount.Issue("USER#alice", 500, Rewards(), "redemption-1", DateTime.UtcNow);

        Assert.Equal(CustomerDiscountStatus.Issued, discount.Status);
    }

    [Fact]
    public void IsRedeemableBy_ShouldReturnTrue_ForTheOwnerBeforeExpiry()
    {
        var discount = CustomerDiscount.Issue("USER#alice", 500, Rewards(), "redemption-1", DateTime.UtcNow);

        Assert.True(discount.IsRedeemableBy("USER#alice", DateTime.UtcNow));
    }

    [Fact]
    public void IsRedeemableBy_ShouldReturnFalse_ForADifferentOwner()
    {
        var discount = CustomerDiscount.Issue("USER#alice", 500, Rewards(), "redemption-1", DateTime.UtcNow);

        Assert.False(discount.IsRedeemableBy("USER#bob", DateTime.UtcNow));
    }

    [Fact]
    public void IsRedeemableBy_ShouldReturnFalse_AfterExpiry()
    {
        var issuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var discount = CustomerDiscount.Issue("USER#alice", 500, Rewards(), "redemption-1", issuedAt);

        Assert.False(discount.IsRedeemableBy("USER#alice", issuedAt.AddDays(91)));
    }

    [Fact]
    public void IsRedeemableBy_ShouldReturnFalse_WhenAlreadyConsumed()
    {
        var discount = CustomerDiscount.Load(
            Guid.NewGuid().ToString(), "USER#alice", 50m, CustomerDiscountStatus.Consumed,
            DateTime.UtcNow.AddDays(1), "redemption-1");

        Assert.False(discount.IsRedeemableBy("USER#alice", DateTime.UtcNow));
    }
}
