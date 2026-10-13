using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Stock.Features.TransferStock;

namespace Inventory.Function;

// Actor is the caller's Cognito sub, injected by the resolver.
public record TransferStockRequest(
    string IdempotencyKey, string ProductId, string FromWarehouseId, string ToWarehouseId, int Quantity,
    string? Note, string Actor);

// AppSync Mutation resolver, Lambda-backed (ADR-0009 criteria 1 and 4, ADR-0052 §7): two balances and
// two ledger rows in one transaction.
public partial class Functions
{
    [LambdaFunction]
    public async Task<StockMovementResponse> TransferStock(TransferStockRequest request, [FromServices] ISender sender)
    {
        var result = await sender.Send(new TransferStockCommand(
            request.IdempotencyKey, request.ProductId, request.FromWarehouseId, request.ToWarehouseId,
            request.Quantity, request.Note, request.Actor));
        return StockMovementResponse.From(result);
    }
}
