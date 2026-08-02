using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

public class GetInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
    InstallmentOptions installmentOptions)
    : IQueryHandler<GetInstallmentPlanQuery, GetInstallmentPlanResult>
{
    public async ValueTask<GetInstallmentPlanResult> Handle(
        GetInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        // Three independent reads keyed off the same product id — none of them needs another's
        // result, so they overlap instead of stacking three round trips onto a query the product
        // page waits through synchronously.
        var priceTask = priceRepository.GetByProductIdAsync(query.ProductId, cancellationToken);
        var gatewayCostTask = gatewayCostRepository.GetByProviderAsync(
            installmentOptions.ActiveProvider, cancellationToken);
        var activeDiscountTask = campaignRepository.GetActiveDiscountForProductAsync(
            query.ProductId, cancellationToken);

        await Task.WhenAll(priceTask, gatewayCostTask, activeDiscountTask);

        var price = await priceTask
            ?? throw new PriceNotFoundException(query.ProductId);

        var gatewayCost = await gatewayCostTask
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var activeDiscount = await activeDiscountTask;
        var discount = activeDiscount is null ? null : DiscountValue.Of(activeDiscount.Type, activeDiscount.Amount);

        var breakdown = InstallmentCalculator.CalculateWithOptionalDiscount(
            price.Cost, price.NominalPrice, gatewayCost, installmentOptions.MinMarginPercent,
            installmentOptions.ValueTiers, discount);

        return new GetInstallmentPlanResult(
            query.ProductId,
            price.NominalPrice,
            breakdown.Price,
            breakdown.CashPrice,
            breakdown.MaxInstallmentsWithoutInterest,
            breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList());
    }
}
