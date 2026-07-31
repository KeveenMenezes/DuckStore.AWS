using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public class GetBasketInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
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

        var cartPlan = InstallmentCalculator.CalculateForCart(
            items, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers);

        return new GetBasketInstallmentPlanResult(
            cartPlan.TotalOriginalPrice,
            cartPlan.Breakdown.Price,
            cartPlan.Breakdown.CashPrice,
            cartPlan.Breakdown.MaxInstallmentsWithoutInterest,
            cartPlan.Breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList());
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
