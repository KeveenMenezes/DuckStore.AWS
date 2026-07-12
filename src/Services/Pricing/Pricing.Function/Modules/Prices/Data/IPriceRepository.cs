namespace Pricing.Function.Modules.Prices.Data;

public interface IPriceRepository
{
    Task<Price?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);

    // BatchGetItem instead of N parallel GetItemAsync calls — used by basket-wide pricing (e.g.
    // installment plan) where the number of distinct products scales with the cart size.
    Task<List<Price>> GetByProductIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    Task PutAsync(Price price, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
