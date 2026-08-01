using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;

namespace Pricing.Function;

public record BasketInstallmentItemRequest(Guid ProductId, int Quantity);

public record GetBasketInstallmentPlanRequest(
    IReadOnlyList<BasketInstallmentItemRequest> Items, string? OwnerId = null, string? DiscountId = null);

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
    [LambdaFunction]
    public async Task<GetBasketInstallmentPlanResponse> GetBasketInstallmentPlan(
        GetBasketInstallmentPlanRequest request,
        [FromServices] ISender sender)
    {
        var query = new GetBasketInstallmentPlanQuery(
            [.. request.Items.Select(i => new BasketInstallmentItem(i.ProductId, i.Quantity))],
            request.OwnerId,
            request.DiscountId);
        var result = await sender.Send(query, CancellationToken.None);
        return new GetBasketInstallmentPlanResponse(
            result.TotalOriginalPrice,
            result.Price,
            result.CashPrice,
            result.MaxInstallmentsWithoutInterest,
            [.. result.InstallmentPlan.Select(e =>
                new BasketInstallmentPlanEntryResponse(e.Count, e.Value, e.TotalValue, e.HasInterest))]);
    }
}
