using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public record BasketInstallmentItem(Guid ProductId, int Quantity);

// OwnerId/DiscountId are both null for the common no-coupon case (ADR-0046 §5) — the resolver only
// populates them when the client sends a discountId, and only after confirming ctx.identity (a
// customer discount is Cognito-only; basketInstallmentPlan itself stays public for guests).
//
// PointsToUse is the in-cart redemption (ADR-0048 §3): capped here, never checked against the
// balance — Pricing doesn't know it (ADR-0046 §1). ExpectedTotal/PaymentMethod/Installments come
// only from the checkout pipeline: with them, the quote also picks the total charged for that
// payment choice and refuses it if it differs from what the customer confirmed (ADR-0048 §4).
// PaymentMethod follows Ordering's enum: Debit = 1, Credit = 2, Cash = 3.
public record GetBasketInstallmentPlanQuery(
    IReadOnlyList<BasketInstallmentItem> Items,
    string? OwnerId = null,
    string? DiscountId = null,
    int PointsToUse = 0,
    decimal? ExpectedTotal = null,
    int? PaymentMethod = null,
    int? Installments = null)
    : IQuery<GetBasketInstallmentPlanResult>;

public record InstallmentPlanEntryDto(int Count, decimal Value, decimal TotalValue, bool HasInterest);

// Price/CashPrice/InstallmentPlan are already net of PointsDiscount. MaxRedeemablePoints is the cap
// for this cart whatever PointsToUse was. ChargedTotal is set only when ExpectedTotal was given.
public record GetBasketInstallmentPlanResult(
    decimal TotalOriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<InstallmentPlanEntryDto> InstallmentPlan,
    int MaxRedeemablePoints = 0,
    decimal PointsDiscount = 0m,
    decimal? ChargedTotal = null);

public class GetBasketInstallmentPlanQueryValidator : IValidator<GetBasketInstallmentPlanQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetBasketInstallmentPlanQuery instance)
    {
        if (instance.Items is null || instance.Items.Count == 0)
        {
            yield return new(nameof(instance.Items), "Items is required");
            yield break;
        }

        for (var i = 0; i < instance.Items.Count; i++)
        {
            var item = instance.Items[i];

            if (item.ProductId == Guid.Empty)
            {
                yield return new($"{nameof(instance.Items)}[{i}].{nameof(item.ProductId)}", "ProductId is required");
            }

            if (item.Quantity <= 0)
            {
                yield return new($"{nameof(instance.Items)}[{i}].{nameof(item.Quantity)}", "Quantity must be greater than zero");
            }
        }

        if (!string.IsNullOrWhiteSpace(instance.DiscountId) && string.IsNullOrWhiteSpace(instance.OwnerId))
        {
            yield return new(nameof(instance.OwnerId), "OwnerId is required when DiscountId is provided");
        }

        if (instance.PointsToUse < 0)
        {
            yield return new(nameof(instance.PointsToUse), "PointsToUse must not be negative");
        }

        // The coupon is on its way out (ADR-0048 supersedes ADR-0046's model); stacking both would
        // let the old coupon slip past the new cap.
        if (instance.PointsToUse > 0 && !string.IsNullOrWhiteSpace(instance.DiscountId))
        {
            yield return new(nameof(instance.PointsToUse), "PointsToUse cannot be combined with a DiscountId");
        }

        if (instance.ExpectedTotal is not null)
        {
            if (instance.ExpectedTotal < 0)
            {
                yield return new(nameof(instance.ExpectedTotal), "ExpectedTotal must not be negative");
            }

            if (instance.PaymentMethod is null)
            {
                yield return new(nameof(instance.PaymentMethod), "PaymentMethod is required when ExpectedTotal is provided");
            }

            if (instance.Installments is null or < 1)
            {
                yield return new(nameof(instance.Installments), "Installments is required when ExpectedTotal is provided");
            }
        }
    }
}
