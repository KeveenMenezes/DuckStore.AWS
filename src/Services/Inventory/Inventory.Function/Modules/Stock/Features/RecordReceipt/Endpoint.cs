using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Stock.Features.RecordReceipt;

namespace Inventory.Function;

// Actor is the caller's Cognito sub, injected by the resolver from ctx.identity, never client input.
public record RecordStockReceiptRequest(
    string IdempotencyKey, string ProductId, string WarehouseId, int Quantity,
    string? InvoiceNumber, string? Supplier, string? Note, string Actor);

// AppSync Mutation resolver, Lambda-backed (ADR-0009 criterion 1, ADR-0052 §7): reads the warehouse and
// the balance before the conditional write. Invoked only by AppSync (and local.ts locally).
public partial class Functions
{
    [LambdaFunction]
    public async Task<StockMovementResponse> RecordStockReceipt(
        RecordStockReceiptRequest request, [FromServices] ISender sender)
    {
        var result = await sender.Send(new RecordReceiptCommand(
            request.IdempotencyKey, request.ProductId, request.WarehouseId, request.Quantity,
            request.InvoiceNumber, request.Supplier, request.Note, request.Actor));
        return StockMovementResponse.From(result);
    }
}
