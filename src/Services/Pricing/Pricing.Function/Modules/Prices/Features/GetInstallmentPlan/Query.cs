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

public class GetInstallmentPlanQueryValidator : AbstractValidator<GetInstallmentPlanQuery>
{
    public GetInstallmentPlanQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage("ProductId is required");
    }
}
