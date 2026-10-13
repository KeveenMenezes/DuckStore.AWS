namespace Inventory.Function.Modules.Stock.Domain;

// What a balance decided: the ledger rows to write, or why it refused.
public sealed class MovementDecision
{
    private MovementDecision(IReadOnlyList<StockMovement> movements, StockRefusal? refusal)
    {
        Movements = movements;
        Refusal = refusal;
    }

    public IReadOnlyList<StockMovement> Movements { get; }
    public StockRefusal? Refusal { get; }
    public bool IsRefused => Refusal is not null;

    public static MovementDecision Recorded(params StockMovement[] movements) => new(movements, null);

    public static MovementDecision Refused(StockRefusal refusal) => new([], refusal);

    public static implicit operator MovementDecision(StockRefusal refusal) => Refused(refusal);
}
