using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

// Treats the whole cart as one virtual transaction: sums Cost/NominalPrice across every item
// (weighted by quantity) and runs the totals through the same cost-floor InstallmentCalculator
// used per-product. This is more correct than summing already-computed per-item plans, since the
// gateway's flat fee is charged once per checkout, not once per item — see ADR-0028.
public class GetBasketInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    InstallmentOptions installmentOptions)
    : IQueryHandler<GetBasketInstallmentPlanQuery, GetBasketInstallmentPlanResult>
{
    public async Task<GetBasketInstallmentPlanResult> Handle(
        GetBasketInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        var productIds = query.Items.Select(item => item.ProductId).ToList();
        var pricesByProductId = (await priceRepository.GetByProductIdsAsync(productIds, cancellationToken))
            .ToDictionary(price => price.Id.Value);

        var prices = query.Items.Select(item =>
        {
            var price = pricesByProductId.TryGetValue(item.ProductId, out var found)
                ? found
                : throw new PriceNotFoundException(item.ProductId);
            return (Price: price, item.Quantity);
        }).ToList();

        var totalCost = prices.Sum(p => p.Price.Cost * p.Quantity);
        var totalOriginalPrice = prices.Sum(p => p.Price.NominalPrice * p.Quantity);

        var gatewayCost = await gatewayCostRepository.GetByProviderAsync(
                installmentOptions.ActiveProvider, cancellationToken)
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var breakdown = InstallmentCalculator.Calculate(
            totalCost, totalOriginalPrice, gatewayCost, installmentOptions.MinMarginPercent,
            installmentOptions.ValueTiers);

        return new GetBasketInstallmentPlanResult(
            totalOriginalPrice,
            breakdown.Price,
            breakdown.CashPrice,
            breakdown.MaxInstallmentsWithoutInterest,
            breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList());
    }
}
