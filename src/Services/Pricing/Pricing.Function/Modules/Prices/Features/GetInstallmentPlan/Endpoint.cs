using Mapster;
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
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<GetInstallmentPlanResponse> GetInstallmentPlan(
        GetInstallmentPlanRequest request,
        [FromServices] ISender sender)
    {
        var query = request.Adapt<GetInstallmentPlanQuery>();
        var result = await sender.Send(query, CancellationToken.None);
        return result.Adapt<GetInstallmentPlanResponse>();
    }
}
