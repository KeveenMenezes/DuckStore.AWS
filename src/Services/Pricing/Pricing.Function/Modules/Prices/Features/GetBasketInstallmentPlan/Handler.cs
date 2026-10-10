using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public class GetBasketInstallmentPlanHandler(
    IPriceRepository priceRepository,
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
    ICustomerDiscountRepository customerDiscountRepository,
    InstallmentOptions installmentOptions,
    RewardOptions rewardOptions)
    : IQueryHandler<GetBasketInstallmentPlanQuery, GetBasketInstallmentPlanResult>
{
    // Ordering's PaymentMethod enum value for cash; every other value is a card.
    private const int CashPaymentMethod = 3;

    public async ValueTask<GetBasketInstallmentPlanResult> Handle(
        GetBasketInstallmentPlanQuery query, CancellationToken cancellationToken)
    {
        var productIds = query.Items.Select(item => item.ProductId).ToList();

        // Four independent reads (prices, campaign discounts, gateway cost, customer coupon): none
        // feeds another's key, so they go out together instead of costing four sequential round
        // trips on a query the customer waits through synchronously.
        var pricesTask = priceRepository.GetByProductIdsAsync(productIds, cancellationToken);
        var discountsTask = campaignRepository.GetActiveDiscountsForProductsAsync(productIds, cancellationToken);
        var gatewayCostTask = gatewayCostRepository.GetByProviderAsync(
            installmentOptions.ActiveProvider, cancellationToken);
        var customerDiscountTask = ResolveCustomerDiscountAsync(query, cancellationToken);

        await Task.WhenAll(pricesTask, discountsTask, gatewayCostTask, customerDiscountTask);

        var pricesByProductId = (await pricesTask).ToDictionary(price => price.Id.Value);
        var discountsByProductId = await discountsTask;

        var items = query.Items.Select(item =>
        {
            var price = pricesByProductId.TryGetValue(item.ProductId, out var found)
                ? found
                : throw new PriceNotFoundException(item.ProductId);
            var discount = discountsByProductId.TryGetValue(item.ProductId, out var active)
                ? DiscountValue.Of(active.Type, active.Amount)
                : null;
            return (Price: price, item.Quantity, Discount: discount);
        }).ToList();

        var gatewayCost = await gatewayCostTask
            ?? throw new GatewayCostNotFoundException(installmentOptions.ActiveProvider);

        var customerDiscountAmount = await customerDiscountTask;

        var cartPlan = InstallmentCalculator.CalculateForCart(
            items, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers,
            customerDiscountAmount);

        // The cap is 20% of the post-campaign card price — the cart's price before any customer
        // discount (ADR-0048 §3). The points discount then goes through the same cart-level,
        // after-campaign path the coupon uses, which also recomputes the cash price and every
        // installment over the net total (ADR-0046 §5).
        var maxRedeemablePoints = rewardOptions.MaxRedeemablePoints(cartPlan.Breakdown.Price);
        if (query.PointsToUse > maxRedeemablePoints)
        {
            throw new PointsAboveCapException(query.PointsToUse, maxRedeemablePoints);
        }

        var pointsDiscount = rewardOptions.PointsDiscount(query.PointsToUse);
        if (pointsDiscount > 0)
        {
            cartPlan = InstallmentCalculator.CalculateForCart(
                items, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers,
                pointsDiscount);
        }

        var chargedTotal = query.ExpectedTotal is { } expectedTotal
            ? ConfirmChargedTotal(cartPlan.Breakdown, expectedTotal, query.PaymentMethod!.Value, query.Installments!.Value)
            : (decimal?)null;

        return new GetBasketInstallmentPlanResult(
            cartPlan.TotalOriginalPrice,
            cartPlan.Breakdown.Price,
            cartPlan.Breakdown.CashPrice,
            cartPlan.Breakdown.MaxInstallmentsWithoutInterest,
            cartPlan.Breakdown.InstallmentPlan
                .Select(e => new InstallmentPlanEntryDto(e.Count, e.Value, e.TotalValue, e.HasInterest))
                .ToList(),
            maxRedeemablePoints,
            pointsDiscount,
            chargedTotal);
    }

    // The total charged for the customer's payment choice, the same figure the cart shows them:
    // cash → CashPrice, one card installment → Price, N installments → that entry's TotalValue
    // (interest included). Compared in whole cents — both sides come from this same calculation,
    // so any difference is a changed price or a tampered client, never rounding (ADR-0048 §4).
    private static decimal ConfirmChargedTotal(
        PricingBreakdown breakdown, decimal expectedTotal, int paymentMethod, int installments)
    {
        var serverTotal =
            paymentMethod == CashPaymentMethod ? breakdown.CashPrice
            : installments == 1 ? breakdown.Price
            : breakdown.InstallmentPlan.FirstOrDefault(e => e.Count == installments)?.TotalValue
                ?? throw new BadRequestException(nameof(installments), installments, "is not an available installment option");

        if (ToCents(serverTotal) != ToCents(expectedTotal))
        {
            throw new PriceChangedException(serverTotal);
        }

        return serverTotal;
    }

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

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
}
