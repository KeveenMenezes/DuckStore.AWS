using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public class GetBasketInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
    ICustomerDiscountRepository customerDiscountRepository,
    InstallmentOptions installmentOptions)
    : IQueryHandler<GetBasketInstallmentPlanQuery, GetBasketInstallmentPlanResult>
{
    public async ValueTask<GetBasketInstallmentPlanResult> Handle(
        GetBasketInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        var productIds = query.Items.Select(item => item.ProductId).ToList();
        var pricesByProductId = (await priceRepository.GetByProductIdsAsync(productIds, cancellationToken))
            .ToDictionary(price => price.Id.Value);

        var discountsByProductId = await GetActiveDiscountsAsync(productIds, cancellationToken);

        var items = query.Items.Select(item =>
        {
            var price = pricesByProductId.TryGetValue(item.ProductId, out var found)
                ? found
                : throw new PriceNotFoundException(item.ProductId);
            return (Price: price, item.Quantity, Discount: discountsByProductId.GetValueOrDefault(item.ProductId));
        }).ToList();

        var gatewayCost = await gatewayCostRepository.GetByProviderAsync(
                installmentOptions.ActiveProvider, cancellationToken)
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var customerDiscountAmount = await ResolveCustomerDiscountAsync(query, cancellationToken);

        var cartPlan = InstallmentCalculator.CalculateForCart(
            items, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers,
            customerDiscountAmount);

        return new GetBasketInstallmentPlanResult(
            cartPlan.TotalOriginalPrice,
            cartPlan.Breakdown.Price,
            cartPlan.Breakdown.CashPrice,
            cartPlan.Breakdown.MaxInstallmentsWithoutInterest,
            cartPlan.Breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList());
    }

    // Null for the common no-coupon case. Validates owner match, Status=Issued and not expired in
    // one call (CustomerDiscount.IsRedeemableBy) — the same undifferentiated rejection either way,
    // so a caller can't learn from the error whether a discountId belongs to someone else, is
    // already spent, or never existed (ADR-0046 §5).
    private async Task<decimal?> ResolveCustomerDiscountAsync(
        GetBasketInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.DiscountId))
        {
            return null;
        }

        var discount = await customerDiscountRepository.GetAsync(
            query.OwnerId!, query.DiscountId, cancellationToken);

        if (discount is null || !discount.IsRedeemableBy(query.OwnerId!, DateTime.UtcNow))
        {
            throw new CustomerDiscountNotRedeemableException(query.DiscountId);
        }

        return discount.Amount;
    }

    // Same per-product lookup GetInstallmentPlan does; there is no batch discount query, so the
    // reads for a cart run in parallel instead of one after another. Deduplicated because the same
    // product may appear on more than one basket line.
    private async Task<Dictionary<Guid, DiscountValue>> GetActiveDiscountsAsync(
        IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var lookups = await Task.WhenAll(productIds.Distinct().Select(async productId =>
        (
            ProductId: productId,
            Discount: await campaignRepository.GetActiveDiscountForProductAsync(productId, cancellationToken)
        )));

        return lookups
            .Where(lookup => lookup.Discount is not null)
            .ToDictionary(
                lookup => lookup.ProductId,
                lookup => DiscountValue.Of(lookup.Discount!.Type, lookup.Discount.Amount));
    }
}
