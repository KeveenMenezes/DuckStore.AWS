namespace Discount.Grpc.Services;

public class DiscountService
    (DiscountContext dbContext, ILogger<DiscountService> logger)
    : DiscountProtoService.DiscountProtoServiceBase
{
    public override async Task<CouponModel> GetDiscount(
        GetDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext
            .Coupons
            .FirstOrDefaultAsync(x => x.ProductName == request.ProductName) ??
            Coupon.CreateNoDiscountCoupon();

        logger.LogInformation(
            @"Discount is retrieved for ProductName: {ProductName}, Amout: {Amout}",
            coupon.ProductName, coupon.Amount);

        var couponModel = coupon.Adapt<CouponModel>();
        return couponModel;
    }

    public override async Task<CouponModel> CreateDiscount(
        CreateDiscountRequest request, ServerCallContext context)
    {
        var coupon = request.Coupon.Adapt<Coupon>() ??
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Invalid argument"));

        await dbContext.Coupons.AddAsync(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Discount is successfully created. ProductName: {ProductName}, Amount: {Amount}",
            coupon.ProductName, coupon.Amount);

        var couponModel = coupon.Adapt<CouponModel>();
        return couponModel;
    }

    public override async Task<CouponModel> UpdateDiscount(
        UpdateDiscountRequest request, ServerCallContext context)
    {
        var coupon = request.Coupon.Adapt<Coupon>() ??
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Invalid argument"));

        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Discount is successfully updante. ProductName: {ProductName}, Amount: {Amount}",
            coupon.ProductName, coupon.Amount);

        var couponModel = coupon.Adapt<CouponModel>();
        return couponModel;
    }

    public override async Task<DeleteDiscountResponse> DeleteDiscount(
        DeleteDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext
            .Coupons
            .FirstOrDefaultAsync(x => x.ProductName == request.ProductName) ??
                throw new RpcException(new Status(
                    StatusCode.NotFound,
                    $"Discount with ProductName={request.ProductName} is not found."));

        dbContext.Coupons.Remove(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            @"Discount is successfully deleted. ProductName: {ProductName}, Amout: {Amout}",
            coupon.ProductName, coupon.Amount);

        return new DeleteDiscountResponse { Success = true };
    }
}
