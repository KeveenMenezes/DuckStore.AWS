namespace Inventory.Function.Modules.Stock.Features.TransferStock;

// Load both balances, delegate to StockBalance.TransferTo, save both rows and both balances in one
// transaction (ADR-0052 §7). Only the destination must still be active when it commits.
public sealed class TransferStockHandler(MovementRecorder recorder, IWarehouseReader warehouses, IStockRepository stock)
    : ICommandHandler<TransferStockCommand, MovementResult>
{
    public ValueTask<MovementResult> Handle(TransferStockCommand command, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            command.ProductId, command.IdempotencyKey, command.Actor, command.Fingerprint(),
            TransferStockCommand.MovementIdsOf,
            async (context, ct) =>
            {
                var from = await warehouses.GetAsync(command.FromWarehouseId, ct);
                if (from is null)
                    return StockAttempt.Refused(StockRefusal.WarehouseNotFound("fromWarehouseId"));
                var to = await warehouses.GetAsync(command.ToWarehouseId, ct);
                if (to is null)
                    return StockAttempt.Refused(StockRefusal.WarehouseNotFound("toWarehouseId"));

                var origin = await stock.LoadAsync(command.ProductId, from, ct);
                var destination = await stock.LoadAsync(command.ProductId, to, ct);
                var decision = origin.TransferTo(destination, command.Quantity, command.Note, context);
                return StockAttempt.From(decision, movements => new StockWrite(
                    [origin, destination], movements,
                    [new WarehouseCondition(from.Id, MustBeActive: false), new WarehouseCondition(to.Id, MustBeActive: true)]));
            },
            cancellationToken);
}
