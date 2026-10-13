namespace Inventory.Function.Modules.Stock.Features.RecordReceipt;

// Load, delegate, save: whether the receipt is allowed and what the balance becomes is
// StockBalance.Receive's call (ADR-0052 §7).
public sealed class RecordReceiptHandler(MovementRecorder recorder, IWarehouseReader warehouses, IStockRepository stock)
    : ICommandHandler<RecordReceiptCommand, MovementResult>
{
    public ValueTask<MovementResult> Handle(RecordReceiptCommand command, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            command.ProductId, command.IdempotencyKey, command.Actor, command.Fingerprint(),
            key => [key.ToString()],
            async (context, ct) =>
            {
                var warehouse = await warehouses.GetAsync(command.WarehouseId, ct);
                if (warehouse is null)
                    return StockAttempt.Refused(StockRefusal.WarehouseNotFound("warehouseId"));

                var balance = await stock.LoadAsync(command.ProductId, warehouse, ct);
                var decision = balance.Receive(command.Quantity, command.InvoiceNumber, command.Supplier, command.Note, context);
                return StockAttempt.From(decision, movements =>
                    new StockWrite([balance], movements, [new WarehouseCondition(warehouse.Id, MustBeActive: true)]));
            },
            cancellationToken);
}
