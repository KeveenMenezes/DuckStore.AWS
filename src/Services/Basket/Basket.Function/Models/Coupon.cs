using BuildingBlocks.Core.DomainModel;

namespace Basket.Function.Models;

// Discount entity inside the Basket aggregate's context (the Discount service was merged
// into Basket — coupons are now read in-process, no cross-service Lambda invoke).
// Id holds the product name: the coupon's natural key and the `coupons` table partition key.
public class Coupon : Entity<string>
{
    public required string Description { get; init; }
    public int Amount { get; init; }

    public static Coupon NoDiscountFor(string productName) =>
        new()
        {
            Id = productName,
            Description = "No Discount",
            Amount = 0
        };
}
