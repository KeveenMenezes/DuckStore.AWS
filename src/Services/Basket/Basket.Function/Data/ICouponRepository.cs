namespace Basket.Function.Data;

public interface ICouponRepository
{
    Task<Coupon?> GetByProductNameAsync(string productName, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
}
