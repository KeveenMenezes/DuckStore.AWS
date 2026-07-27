using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

namespace Pricing.Function;

public record GetInstallmentPlanRequest(Guid ProductId);

public record InstallmentPlanEntryResponse(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public record GetInstallmentPlanResponse(
    Guid ProductId,
    decimal OriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<InstallmentPlanEntryResponse> InstallmentPlan);

// AppSync Query resolver (Lambda-backed per ADR-0009 — a non-trivial calculation, not a key
// lookup).
public partial class Functions
{
    [LambdaFunction]
    public async Task<GetInstallmentPlanResponse> GetInstallmentPlan(
        GetInstallmentPlanRequest request,
        [FromServices] ISender sender)
    {
        var query = new GetInstallmentPlanQuery(request.ProductId);
        var result = await sender.Send(query, CancellationToken.None);
        return new GetInstallmentPlanResponse(
            result.ProductId,
            result.OriginalPrice,
            result.Price,
            result.CashPrice,
            result.MaxInstallmentsWithoutInterest,
            [.. result.InstallmentPlan.Select(e =>
                new InstallmentPlanEntryResponse(e.Count, e.Value, e.TotalValue, e.HasInterest))]);
    }
}
