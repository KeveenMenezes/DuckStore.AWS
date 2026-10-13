namespace Inventory.Function.Modules.Stock.Features.RecordAdjustment;

// Load, delegate, save (ADR-0052 §7). Only an increase relies on the warehouse being active, so only
// then is Active re-checked in the transaction.
public sealed class RecordAdjustmentHandler(MovementRecorder recorder, IWarehouseReader warehouses, IStockRepository stock)
    : ICommandHandler<RecordAdjustmentCommand, MovementResult>
{
    public ValueTask<MovementResult> Handle(RecordAdjustmentCommand command, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            command.ProductId, command.IdempotencyKey, command.Actor, command.Fingerprint(),
            key => [key.ToString()],
            async (context, ct) =>
            {
                var warehouse = await warehouses.GetAsync(command.WarehouseId, ct);
                if (warehouse is null)
                    return StockAttempt.Refused(StockRefusal.WarehouseNotFound("warehouseId"));

                var balance = await stock.LoadAsync(command.ProductId, warehouse, ct);
                var decision = balance.Adjust(command.Direction, command.Quantity, command.Reason, command.Note, context);
                var increase = command.Direction == StockAdjustmentDirection.Increase;
                return StockAttempt.From(decision, movements =>
                    new StockWrite([balance], movements, [new WarehouseCondition(warehouse.Id, MustBeActive: increase)]));
            },
            cancellationToken);
}
