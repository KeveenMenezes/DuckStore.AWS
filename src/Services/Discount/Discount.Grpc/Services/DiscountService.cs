namespace Discount.Grpc.Services;

public partial class DiscountService
    (ICouponRepository couponRepository, ILogger<DiscountService> logger)
    : DiscountProtoService.DiscountProtoServiceBase
{
    public override async Task<CouponModel> GetDiscount(
        GetDiscountRequest request, ServerCallContext context)
    {
        var coupon = await couponRepository.GetByProductNameAsync(request.ProductName, context.CancellationToken) ??
            Coupon.CreateNoDiscountCoupon();

        LogDiscountIsRetrievedForProductName(coupon.ProductName, coupon.Amount);

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

        await couponRepository.AddAsync(coupon, context.CancellationToken);

        LogDiscountIsSuccessfullyCreatedProductName(coupon.ProductName, coupon.Amount);

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

        await couponRepository.UpdateAsync(coupon, context.CancellationToken);

        LogDiscountIsSuccessfullyUpdatedProductName(coupon.ProductName, coupon.Amount);

        var couponModel = coupon.Adapt<CouponModel>();
        return couponModel;
    }

    public override async Task<DeleteDiscountResponse> DeleteDiscount(
        DeleteDiscountRequest request, ServerCallContext context)
    {
        var coupon = await couponRepository.GetByProductNameAsync(request.ProductName, context.CancellationToken) ??
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Discount with ProductName={request.ProductName} is not found."));

        await couponRepository.DeleteAsync(request.ProductName, context.CancellationToken);

        LogDiscountIsSuccessfullyDeletedProductName(coupon.ProductName, coupon.Amount);

        return new DeleteDiscountResponse { Success = true };
    }

    [LoggerMessage(LogLevel.Information, @"Discount is retrieved for ProductName: {ProductName}, Amount: {Amount}")]
    partial void LogDiscountIsRetrievedForProductName(string productName, int amount);

    [LoggerMessage(LogLevel.Information, "Discount is successfully update. ProductName: {ProductName}, Amount: {Amount}")]
    partial void LogDiscountIsSuccessfullyUpdatedProductName(string productName, int amount);

    [LoggerMessage(LogLevel.Information, @"Discount is successfully deleted. ProductName: {ProductName}, Amount: {amount}")]
    partial void LogDiscountIsSuccessfullyDeletedProductName(string productName, int amount);

    [LoggerMessage(LogLevel.Information, "Discount is successfully created. ProductName: {ProductName}, Amount: {Amount}")]
    partial void LogDiscountIsSuccessfullyCreatedProductName(string productName, int amount);
}
