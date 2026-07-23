using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

public class GetInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
    InstallmentOptions installmentOptions)
    : IQueryHandler<GetInstallmentPlanQuery, GetInstallmentPlanResult>
{
    public async Task<GetInstallmentPlanResult> Handle(
        GetInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        var price = await priceRepository.GetByProductIdAsync(query.ProductId, cancellationToken)
            ?? throw new PriceNotFoundException(query.ProductId);

        var gatewayCost = await gatewayCostRepository.GetByProviderAsync(
                installmentOptions.ActiveProvider, cancellationToken)
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var activeDiscount = await campaignRepository.GetActiveDiscountForProductAsync(
            query.ProductId, cancellationToken);
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
