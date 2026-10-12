using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.Function;

public record BasketInstallmentItemRequest(Guid ProductId, int Quantity);

public record GetBasketInstallmentPlanRequest(
    IReadOnlyList<BasketInstallmentItemRequest> Items,
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

// One cart line's unit price as Pricing allocates it (ADR-0050 §5) — what the order records.
public record BasketLineResponse(Guid ProductId, int Quantity, decimal UnitPrice);

public record GetBasketInstallmentPlanResponse(
    decimal TotalOriginalPrice,
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    IReadOnlyList<BasketInstallmentPlanEntryResponse> InstallmentPlan,
    int MaxRedeemablePoints,
    decimal PointsDiscount,
    decimal? ChargedTotal,
    QuoteRejectionResponse? Rejection = null,
    IReadOnlyList<BasketLineResponse>? Lines = null);

// AppSync Query resolver (Lambda-backed per ADR-0009 — a non-trivial calculation over the whole
// cart, not a key lookup), and the Quote step of the checkout saga (ADR-0048 §4, ADR-0050).
public partial class Functions
{
    [LambdaFunction]
    public async Task<GetBasketInstallmentPlanResponse> GetBasketInstallmentPlan(
        GetBasketInstallmentPlanRequest request,
        [FromServices] ISender sender)
    {
        var query = new GetBasketInstallmentPlanQuery(
            [.. request.Items.Select(i => new BasketInstallmentItem(i.ProductId, i.Quantity))],
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
                result.ChargedTotal,
                Lines: [.. result.Lines.Select(l => new BasketLineResponse(l.ProductId, l.Quantity, l.UnitPrice))]);
        }
        catch (QuoteRejectedException ex)
        {
            return Rejected(new QuoteRejectionResponse(
                ex.ErrorType,
                ex.Message,
                (ex as PriceChangedException)?.ServerTotal,
                (ex as PointsAboveCapException)?.MaxRedeemablePoints));
        }
        // Inputs a client can get wrong (its stored cart, the payment choice) are refusals too: a
        // thrown error would reach the checkout saga as an ambiguous fault (ADR-0050 §8).
        catch (PriceNotFoundException ex)
        {
            return Rejected(new QuoteRejectionResponse("ProductUnavailable", ex.Message, null, null));
        }
        catch (Exception ex) when (ex is BadRequestException or ValidationException)
        {
            return Rejected(new QuoteRejectionResponse("BadRequest", ex.Message, null, null));
        }
    }

    private static GetBasketInstallmentPlanResponse Rejected(QuoteRejectionResponse rejection) =>
        new(0m, 0m, 0m, 0, [], 0, 0m, null, rejection);
}
