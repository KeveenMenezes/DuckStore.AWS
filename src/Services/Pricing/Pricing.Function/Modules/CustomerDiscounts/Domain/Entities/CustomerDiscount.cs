using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.CustomerDiscounts.Domain.Entities;

// A customer-scoped reward, minted from a Challenges points redemption (ADR-0046 §4) — a distinct
// shape from the product-scoped campaign discount (ADR-0026): PK OwnerId, SK Id (a fresh DiscountId),
// applies to a cart regardless of what is in it. Amount is decided here and nowhere else — the
// PointsRedeemedEvent that triggers Issue carries a point quantity, never a currency value
// (ADR-0046 §1).
public class CustomerDiscount : Aggregate<string>
{
    public string OwnerId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public CustomerDiscountStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string SourceRedemptionId { get; private set; } = default!;

    public static CustomerDiscount Issue(
        string ownerId, int points, RewardOptions rewardOptions, string sourceRedemptionId, DateTime issuedAt) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            OwnerId = ownerId,
            Amount = rewardOptions.ConvertToCurrency(points),
            Status = CustomerDiscountStatus.Issued,
            ExpiresAt = issuedAt.AddDays(rewardOptions.ExpiryDays),
            SourceRedemptionId = sourceRedemptionId,
            CreatedAt = issuedAt
        };

    // Expiry is checked at read time and is authoritative; the table's TTL on ExpiresAt only
    // clears dead rows eventually (ADR-0044 precedent) — correctness must not depend on it firing.
    public bool IsRedeemableBy(string ownerId, DateTime now) =>
        OwnerId == ownerId && Status == CustomerDiscountStatus.Issued && now < ExpiresAt;

    public static CustomerDiscount Load(
        string id, string ownerId, decimal amount, CustomerDiscountStatus status, DateTime expiresAt,
        string sourceRedemptionId, DateTime? createdAt = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Amount = amount,
            Status = status,
            ExpiresAt = expiresAt,
            SourceRedemptionId = sourceRedemptionId,
            CreatedAt = createdAt
        };
}
