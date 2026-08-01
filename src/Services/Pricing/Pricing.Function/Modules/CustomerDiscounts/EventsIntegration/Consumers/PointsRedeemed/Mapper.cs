using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PointsRedeemed;

public static class PointsRedeemedMapper
{
    public static CustomerDiscount ToCustomerDiscount(PointsRedeemedEvent evt, RewardOptions rewardOptions) =>
        CustomerDiscount.Issue(evt.OwnerId, evt.Points, rewardOptions, evt.RedemptionId, DateTime.UtcNow);

    // Prefixed so a redemption id can never collide with an evt.Id-keyed row from another consumer
    // sharing the pricing-processed-events table.
    public static string ToInboxKey(PointsRedeemedEvent evt) => $"PointsRedeemed#{evt.RedemptionId}";
}
