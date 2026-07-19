using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Domain.ValueObjects;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Campaigns.Domain.Entities;

// Groups a discount by event (business rule #3 — e.g. "Black Friday") for batch activation across
// N products. Validity (StartsAt/EndsAt) is checked at read time by consumers of the
// product-discounts projection, not by a scheduler (ADR-0026).
public class Campaign : Aggregate<CampaignId>
{
    private readonly List<ProductId> _productIds = [];

    public string Name { get; private set; } = default!;
    public DiscountValue Discount { get; private set; } = default!;
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public bool IsCancelled { get; private set; }
    public IReadOnlyList<ProductId> ProductIds => _productIds.AsReadOnly();

    public static Campaign Create(
        CampaignId id,
        string name,
        DiscountValue discount,
        DateTime startsAt,
        DateTime endsAt,
        IEnumerable<ProductId> productIds)
    {
        if (endsAt <= startsAt)
        {
            throw new CampaignPeriodBadRequestException(startsAt, endsAt);
        }

        var ids = productIds.ToList();
        if (ids.Count == 0)
        {
            throw new BadRequestException("ProductIds", ids.Count, "must include at least one product");
        }

        var campaign = new Campaign
        {
            Id = id,
            Name = name,
            Discount = discount,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CreatedAt = DateTime.UtcNow
        };

        campaign._productIds.AddRange(ids);

        return campaign;
    }

    public void Cancel() => IsCancelled = true;

    public static Campaign Load(
        Guid id,
        string name,
        DiscountType discountType,
        decimal discountAmount,
        DateTime startsAt,
        DateTime endsAt,
        bool isCancelled,
        IEnumerable<Guid> productIds,
        DateTime? createdAt = null)
    {
        var campaign = new Campaign
        {
            Id = CampaignId.Of(id),
            Name = name,
            Discount = DiscountValue.Of(discountType, discountAmount),
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsCancelled = isCancelled,
            CreatedAt = createdAt
        };

        campaign._productIds.AddRange(productIds.Select(ProductId.Of));

        return campaign;
    }
}
