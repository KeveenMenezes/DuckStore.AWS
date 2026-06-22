namespace Discount.Grpc.Models;

public class Coupon
{
    public int Id { get; set; }
    public required string ProductName { get; init; }
    public required string Description { get; init; }
    public int Amount { get; init; }

    public static Coupon CreateNoDiscountCoupon()
    {
        return new Coupon
        {
            ProductName = "No Discount",
            Amount = 0,
            Description = "No Discount Description",
        };
    }
}
