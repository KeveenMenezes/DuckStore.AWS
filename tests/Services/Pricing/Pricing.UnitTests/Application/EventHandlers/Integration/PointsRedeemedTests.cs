using BuildingBlocks.Messaging.Events;
using Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PointsRedeemed;

namespace Pricing.UnitTests.Application.EventHandlers.Integration;

public class PointsRedeemedMapperTests
{
    [Fact]
    public void ToCustomerDiscount_ShouldConvertPointsUsingRewardOptions_AndCarryTheRedemptionId()
    {
        var evt = new PointsRedeemedEvent { OwnerId = "USER#alice", RedemptionId = "redemption-1", Points = 500 };
        var rewardOptions = new RewardOptions { PointsPerUnit = 100, CurrencyPerUnit = 10m, ExpiryDays = 90 };

        var discount = PointsRedeemedMapper.ToCustomerDiscount(evt, rewardOptions);

        Assert.Equal("USER#alice", discount.OwnerId);
        Assert.Equal("redemption-1", discount.SourceRedemptionId);
        Assert.Equal(50m, discount.Amount);
    }

    [Fact]
    public void ToInboxKey_ShouldBeStableAcrossRepublishesOfTheSameRedemption()
    {
        // The stream publisher re-publishes with a fresh EventBridge id whenever its batch is
        // retried or bisected, so keying the inbox on evt.Id would let one redemption mint a
        // second discount — and nothing downstream would catch it, since the discount's own
        // attribute_not_exists guard protects a Guid minted on that very invocation.
        var first = new PointsRedeemedEvent { OwnerId = "USER#alice", RedemptionId = "redemption-1", Points = 500 };
        var republished = new PointsRedeemedEvent { OwnerId = "USER#alice", RedemptionId = "redemption-1", Points = 500 };
        var other = new PointsRedeemedEvent { OwnerId = "USER#alice", RedemptionId = "redemption-2", Points = 500 };

        Assert.Equal(PointsRedeemedMapper.ToInboxKey(first), PointsRedeemedMapper.ToInboxKey(republished));
        Assert.NotEqual(PointsRedeemedMapper.ToInboxKey(first), PointsRedeemedMapper.ToInboxKey(other));
        Assert.Contains("redemption-1", PointsRedeemedMapper.ToInboxKey(first));
    }
}

public class PointsRedeemedHandlerTests
{
    [Fact]
    public void BuildIssueTransactItems_ShouldTargetCustomerDiscountsTable()
    {
        var discount = CustomerDiscount.Issue(
            "USER#alice", 500, new RewardOptions(), "redemption-1", DateTime.UtcNow);

        var items = PointsRedeemedHandler.BuildIssueTransactItems(discount);

        Assert.Single(items);
        Assert.Equal("customer-discounts", items[0].Put.TableName);
    }
}
