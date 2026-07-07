namespace Pricing.Function.Modules.Prices.Data;

public interface IPriceRepository
{
    Task<Price?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task PutAsync(Price price, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
