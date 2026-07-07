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
}
