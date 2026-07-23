using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

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

        var gatewayCost = await gatewayCostRepository.GetByProviderAsync(
                installmentOptions.ActiveProvider, cancellationToken)
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var cartPlan = InstallmentCalculator.CalculateForCart(
            prices, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers);

        return new GetBasketInstallmentPlanResult(
            cartPlan.TotalOriginalPrice,
            cartPlan.Breakdown.Price,
            cartPlan.Breakdown.CashPrice,
            cartPlan.Breakdown.MaxInstallmentsWithoutInterest,
            cartPlan.Breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList());
    }
}
