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

public class GetBasketInstallmentPlanQueryValidator : AbstractValidator<GetBasketInstallmentPlanQuery>
{
    public GetBasketInstallmentPlanQueryValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Items is required");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("ProductId is required");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero");
        });
    }
}
