using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

public record GetInstallmentPlanQuery(Guid ProductId) : IQuery<GetInstallmentPlanResult>;

public record InstallmentPlanEntryDto(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public record GetInstallmentPlanResult(
    Guid ProductId,
    decimal OriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<InstallmentPlanEntryDto> InstallmentPlan);

public class GetInstallmentPlanQueryValidator : IValidator<GetInstallmentPlanQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetInstallmentPlanQuery instance)
    {
        if (instance.ProductId == Guid.Empty)
        {
            yield return new(nameof(instance.ProductId), "ProductId is required");
        }
    }
}
