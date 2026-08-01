using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public record BasketInstallmentItem(Guid ProductId, int Quantity);

// OwnerId/DiscountId are both null for the common no-coupon case (ADR-0046 §5) — the resolver only
// populates them when the client sends a discountId, and only after confirming ctx.identity (a
// customer discount is Cognito-only; basketInstallmentPlan itself stays public for guests).
public record GetBasketInstallmentPlanQuery(
    IReadOnlyList<BasketInstallmentItem> Items, string? OwnerId = null, string? DiscountId = null)
    : IQuery<GetBasketInstallmentPlanResult>;

public record InstallmentPlanEntryDto(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public record GetBasketInstallmentPlanResult(
    decimal TotalOriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<InstallmentPlanEntryDto> InstallmentPlan);

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
    }
}
