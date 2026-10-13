namespace Inventory.Function.Modules.Stock.Features;

// The ledger rows a movement recorded (or replayed), or why it was refused (ADR-0052 §7).
public sealed record MovementResult(IReadOnlyList<StockMovement> Movements, StockRefusal? Refusal)
{
    public static MovementResult Recorded(IReadOnlyList<StockMovement> movements) => new(movements, null);

    public static MovementResult Refused(StockRefusal refusal) => new([], refusal);
}

// One try at a movement: either a refusal, or the write to attempt.
public sealed record StockAttempt(StockRefusal? Refusal, StockWrite? Write)
{
    public static StockAttempt Refused(StockRefusal refusal) => new(refusal, null);

    public static StockAttempt Writing(StockWrite write) => new(null, write);

    public static StockAttempt From(MovementDecision decision, Func<IReadOnlyList<StockMovement>, StockWrite> write) =>
        decision.IsRefused ? Refused(decision.Refusal!) : Writing(write(decision.Movements));
}
