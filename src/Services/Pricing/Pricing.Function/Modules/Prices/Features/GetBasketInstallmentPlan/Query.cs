using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

public record BasketInstallmentItem(Guid ProductId, int Quantity);

public record GetBasketInstallmentPlanQuery(IReadOnlyList<BasketInstallmentItem> Items)
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
    }
}
