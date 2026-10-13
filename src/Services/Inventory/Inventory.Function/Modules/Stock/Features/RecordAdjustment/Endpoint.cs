using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Stock.Features.RecordAdjustment;

namespace Inventory.Function;

// Direction and Reason arrive as the GraphQL enum names. Actor is the caller's Cognito sub, injected by
// the resolver.
public record RecordStockAdjustmentRequest(
    string IdempotencyKey, string ProductId, string WarehouseId, string Direction, int Quantity,
    string Reason, string? Note, string Actor);

// AppSync Mutation resolver, Lambda-backed (ADR-0009 criterion 1, ADR-0052 §7).
public partial class Functions
{
    [LambdaFunction]
    public async Task<StockMovementResponse> RecordStockAdjustment(
        RecordStockAdjustmentRequest request, [FromServices] ISender sender)
    {
        // Enum.IsDefined: TryParse alone accepts numeric strings such as "7".
        if (!Enum.TryParse<StockAdjustmentDirection>(request.Direction, ignoreCase: false, out var direction) || !Enum.IsDefined(direction))
            return StockMovementResponse.Refused(StockRefusal.BadRequest("direction", "direction must be Increase or Decrease"));
        if (!Enum.TryParse<StockAdjustmentReason>(request.Reason, ignoreCase: false, out var reason) || !Enum.IsDefined(reason))
            return StockMovementResponse.Refused(StockRefusal.BadRequest("reason", "reason must be Count, Loss, Damage, Return or Other"));

        var result = await sender.Send(new RecordAdjustmentCommand(
            request.IdempotencyKey, request.ProductId, request.WarehouseId, direction, request.Quantity,
            reason, request.Note, request.Actor));
        return StockMovementResponse.From(result);
    }
}
