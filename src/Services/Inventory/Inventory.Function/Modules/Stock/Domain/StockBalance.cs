namespace Inventory.Function.Modules.Stock.Domain;

// The stock of one product in one warehouse, and the only place its rules live (ADR-0052 §7):
// quantities are 1-100,000, Available never goes negative, a receipt needs an invoice, an inactive
// warehouse only gives stock out, and every change produces its ledger row. Nothing is applied when a
// movement is refused.
public sealed class StockBalance
{
    public const int MaxQuantity = 100_000;
    private const int MaxInvoiceNumber = 60;
    private const int MaxSupplier = 120;
    private const int MaxNote = 500;

    private StockBalance(string productId, WarehouseSnapshot warehouse, int available, int reserved, int? readAvailable)
    {
        ProductId = productId;
        Warehouse = warehouse;
        Available = available;
        Reserved = reserved;
        ReadAvailable = readAvailable;
    }

    public string ProductId { get; }
    public WarehouseSnapshot Warehouse { get; }
    public int Available { get; private set; }
    public int Reserved { get; }

    // What is physically in the warehouse: the ledger adds up to this (ADR-0052 §7), and a reservation
    // only moves units between Available and Reserved.
    public int OnHand => Available + Reserved;

    // Available as read from the table, or null when the row doesn't exist yet: the condition the save
    // writes against, so a concurrent movement makes it retry instead of overwriting (ADR-0052 §7).
    public int? ReadAvailable { get; }

    public static StockBalance Load(string productId, WarehouseSnapshot warehouse, int available, int reserved) =>
        new(productId, warehouse, available, reserved, available);

    public static StockBalance Empty(string productId, WarehouseSnapshot warehouse) =>
        new(productId, warehouse, 0, 0, null);

    public MovementDecision Receive(int quantity, string? invoiceNumber, string? supplier, string? note, MovementContext context)
    {
        var invoice = Clean(invoiceNumber);
        var cleanSupplier = Clean(supplier);
        var cleanNote = Clean(note);

        if (QuantityRefusal(quantity) is { } badQuantity) return badQuantity;
        if (invoice is null || invoice.Length > MaxInvoiceNumber)
            return StockRefusal.BadRequest("invoiceNumber", $"invoiceNumber is required, at most {MaxInvoiceNumber} characters");
        if (cleanSupplier?.Length > MaxSupplier)
            return StockRefusal.BadRequest("supplier", $"supplier must be at most {MaxSupplier} characters");
        if (NoteRefusal(cleanNote) is { } badNote) return badNote;
        if (!Warehouse.Active) return StockRefusal.WarehouseInactive("warehouseId", Warehouse.Code);

        Available += quantity;
        return MovementDecision.Recorded(Row(
            context.IdempotencyKey.ToString(), StockMovementType.Receipt, quantity, context,
            invoiceNumber: invoice, supplier: cleanSupplier, note: cleanNote));
    }

    public MovementDecision Adjust(
        StockAdjustmentDirection direction, int quantity, StockAdjustmentReason reason, string? note, MovementContext context)
    {
        var cleanNote = Clean(note);

        if (QuantityRefusal(quantity) is { } badQuantity) return badQuantity;
        if (NoteRefusal(cleanNote) is { } badNote) return badNote;
        if (reason == StockAdjustmentReason.Other && cleanNote is null)
            return StockRefusal.BadRequest("note", "note is required when the reason is Other");

        var delta = direction == StockAdjustmentDirection.Increase ? quantity : -quantity;
        // An inactive warehouse can still be emptied, never filled.
        if (delta > 0 && !Warehouse.Active) return StockRefusal.WarehouseInactive("warehouseId", Warehouse.Code);
        if (Available + delta < 0) return StockRefusal.InsufficientStock(Available);

        Available += delta;
        return MovementDecision.Recorded(Row(
            context.IdempotencyKey.ToString(), StockMovementType.Adjustment, delta, context,
            reason: reason, note: cleanNote));
    }

    // Moves stock from this balance to the same product's balance in another warehouse. The origin may
    // be inactive (that is how a warehouse is emptied); the destination must be active.
    public MovementDecision TransferTo(StockBalance destination, int quantity, string? note, MovementContext context)
    {
        if (destination.ProductId != ProductId)
            throw new InvalidOperationException("A transfer moves one product between two of its balances.");

        var cleanNote = Clean(note);

        if (QuantityRefusal(quantity) is { } badQuantity) return badQuantity;
        if (NoteRefusal(cleanNote) is { } badNote) return badNote;
        if (destination.Warehouse.Id == Warehouse.Id)
            return StockRefusal.BadRequest("toWarehouseId", "toWarehouseId must differ from fromWarehouseId");
        if (!destination.Warehouse.Active)
            return StockRefusal.WarehouseInactive("toWarehouseId", destination.Warehouse.Code);
        if (Available < quantity) return StockRefusal.InsufficientStock(Available);

        var transferId = context.IdempotencyKey.ToString();
        Available -= quantity;
        destination.Available += quantity;
        return MovementDecision.Recorded(
            Row($"{transferId}#OUT", StockMovementType.TransferOut, -quantity, context,
                note: cleanNote, transferId: transferId, counterpart: destination.Warehouse.Id),
            destination.Row($"{transferId}#IN", StockMovementType.TransferIn, quantity, context,
                note: cleanNote, transferId: transferId, counterpart: Warehouse.Id));
    }

    private StockMovement Row(
        string movementId, StockMovementType type, int quantity, MovementContext context,
        string? invoiceNumber = null, string? supplier = null, StockAdjustmentReason? reason = null,
        string? note = null, string? transferId = null, string? counterpart = null) =>
        new(ProductId, movementId, Warehouse.Id, Warehouse.Code, type, quantity, OnHand,
            invoiceNumber, supplier, reason, note, transferId, counterpart,
            context.PayloadHash, context.At, context.Actor);

    private static StockRefusal? QuantityRefusal(int quantity) =>
        quantity is < 1 or > MaxQuantity
            ? StockRefusal.BadRequest("quantity", $"quantity must be an integer between 1 and {MaxQuantity}")
            : null;

    private static StockRefusal? NoteRefusal(string? note) =>
        note?.Length > MaxNote ? StockRefusal.BadRequest("note", $"note must be at most {MaxNote} characters") : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
