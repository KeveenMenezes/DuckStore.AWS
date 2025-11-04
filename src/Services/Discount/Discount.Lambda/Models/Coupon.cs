namespace Discount.Lambda.Models;

public class Coupon
{
    public int Id { get; set; }
    public required string ProductName { get; set; }
    public required string Description { get; set; }
    public int Amount { get; set; }

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
