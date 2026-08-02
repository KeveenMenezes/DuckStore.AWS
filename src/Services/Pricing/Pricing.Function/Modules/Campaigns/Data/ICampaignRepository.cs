namespace Pricing.Function.Modules.Campaigns.Data;

public sealed record ActiveDiscount(DiscountType Type, decimal Amount);

public interface ICampaignRepository
{
    Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task CancelAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    // Reads the denormalized "product-discounts" projection (ADR-0026) — the same one AppSync's
    // currentDiscountForProduct resolver reads directly. Returns null when no discount is enrolled
    // or the enrolled one has expired (StartsAt/EndsAt checked at read time, no scheduler).
    Task<ActiveDiscount?> GetActiveDiscountForProductAsync(Guid productId, CancellationToken cancellationToken = default);

    // Batch sibling of the above, for the cart: "product-discounts" is keyed by ProductId alone, so
    // a whole basket resolves in one BatchGetItem instead of one GetItem per line. Same read-time
    // expiry rule; products with no discount in force are simply absent from the result.
    Task<IReadOnlyDictionary<Guid, ActiveDiscount>> GetActiveDiscountsForProductsAsync(
        IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
}
