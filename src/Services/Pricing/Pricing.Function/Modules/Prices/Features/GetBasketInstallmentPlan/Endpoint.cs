using Mapster;
using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

namespace Pricing.Function;

public record BasketInstallmentItemRequest(Guid ProductId, int Quantity);

public record GetBasketInstallmentPlanRequest(IReadOnlyList<BasketInstallmentItemRequest> Items);

public record BasketInstallmentPlanEntryResponse(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public record GetBasketInstallmentPlanResponse(
    decimal TotalOriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<BasketInstallmentPlanEntryResponse> InstallmentPlan);

// AppSync Query resolver (Lambda-backed per ADR-0009 — a non-trivial calculation over the whole
// cart, not a key lookup).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<GetBasketInstallmentPlanResponse> GetBasketInstallmentPlan(
        GetBasketInstallmentPlanRequest request,
        [FromServices] ISender sender)
    {
        var query = request.Adapt<GetBasketInstallmentPlanQuery>();
        var result = await sender.Send(query, CancellationToken.None);
        return result.Adapt<GetBasketInstallmentPlanResponse>();
    }
}
