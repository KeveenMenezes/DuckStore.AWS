namespace Discount.Function;

public record GetDiscountRequest(string ProductName);

public record GetDiscountResponse(string ProductName, string Description, int Amount);

// Invoked directly via the AWS Lambda Invoke API (called by Basket on checkout). Looks up the
// coupon for a product and returns its discount, falling back to a no-discount coupon when none exists.
public class Functions
{
    [LambdaFunction]
    public async Task<GetDiscountResponse> GetDiscount(
        GetDiscountRequest request,
        [FromServices] ICouponRepository couponRepository)
    {
        var coupon = await couponRepository.GetByProductNameAsync(request.ProductName) ??
            Coupon.CreateNoDiscountCoupon();

        return new GetDiscountResponse(coupon.ProductName, coupon.Description, coupon.Amount);
    }
}
