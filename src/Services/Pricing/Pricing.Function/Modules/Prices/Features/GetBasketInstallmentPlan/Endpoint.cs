using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function;

public record BasketInstallmentItemRequest(Guid ProductId, int Quantity);

public record GetBasketInstallmentPlanRequest(
    IReadOnlyList<BasketInstallmentItemRequest> Items,
    string? OwnerId = null,
    string? DiscountId = null,
    int PointsToUse = 0,
    decimal? ExpectedTotal = null,
    int? PaymentMethod = null,
    int? Installments = null);

public record BasketInstallmentPlanEntryResponse(int Count, decimal Value, decimal TotalValue, bool HasInterest);

// A quote Pricing refused (PointsAboveCap, PriceChanged). Returned as data rather than thrown out of
// the Lambda: AppSync reports an unhandled Lambda error without its type or fields, and the SPA must
// branch on ErrorType and read ServerTotal/MaxRedeemablePoints (ADR-0048 §3/§4). The resolvers turn
// it into a GraphQL error with errorType and errorInfo.
public record QuoteRejectionResponse(
    string ErrorType, string Message, decimal? ServerTotal, int? MaxRedeemablePoints);

public record GetBasketInstallmentPlanResponse(
    decimal TotalOriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<BasketInstallmentPlanEntryResponse> InstallmentPlan,
    int MaxRedeemablePoints,
    decimal PointsDiscount,
    decimal? ChargedTotal,
    QuoteRejectionResponse? Rejection = null);

// AppSync Query resolver (Lambda-backed per ADR-0009 — a non-trivial calculation over the whole
// cart, not a key lookup), and the quote step of the checkoutBasket pipeline (ADR-0048 §4).
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
            request.DiscountId,
            request.PointsToUse,
            request.ExpectedTotal,
            request.PaymentMethod,
            request.Installments);

        try
        {
            var result = await sender.Send(query, CancellationToken.None);
            return new GetBasketInstallmentPlanResponse(
                result.TotalOriginalPrice,
                result.Price,
                result.CashPrice,
                result.MaxInstallmentsWithoutInterest,
                [.. result.InstallmentPlan.Select(e =>
                    new BasketInstallmentPlanEntryResponse(e.Count, e.Value, e.TotalValue, e.HasInterest))],
                result.MaxRedeemablePoints,
                result.PointsDiscount,
                result.ChargedTotal);
        }
        catch (QuoteRejectedException ex)
        {
            return new GetBasketInstallmentPlanResponse(
                0m, 0m, 0m, 0, [], 0, 0m, null,
                new QuoteRejectionResponse(
                    ex.ErrorType,
                    ex.Message,
                    (ex as PriceChangedException)?.ServerTotal,
                    (ex as PointsAboveCapException)?.MaxRedeemablePoints));
        }
    }
}
