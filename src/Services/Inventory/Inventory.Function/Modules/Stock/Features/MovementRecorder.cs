namespace Inventory.Function.Modules.Stock.Features;

// The part every movement shares (ADR-0052 §7): answer a replay from the ledger, otherwise read, let the
// balance decide, write once, and when a concurrent movement changed what was read, read and decide
// again. The handlers supply only "what to read and which balance method to call".
public sealed class MovementRecorder(IStockRepository stock, TimeProvider timeProvider)
{
    public const int MaxAttempts = 3;

    public async ValueTask<MovementResult> RecordAsync(
        string productId,
        string idempotencyKey,
        string actor,
        string fingerprint,
        Func<Guid, IReadOnlyList<string>> movementIdsOf,
        Func<MovementContext, CancellationToken, Task<StockAttempt>> attempt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new InvalidOperationException("A movement needs the caller's identity from the token.");
        if (!Guid.TryParse(idempotencyKey, out var key))
            return MovementResult.Refused(StockRefusal.BadRequest("idempotencyKey", "idempotencyKey must be a UUID"));

        var movementIds = movementIdsOf(key);
        // Checked before deciding: a replay of a decrease must return the recorded movement, not be
        // refused for the stock that movement itself consumed.
        if (await ReplayAsync(productId, movementIds, fingerprint, cancellationToken) is { } replay)
            return replay;

        var context = new MovementContext(key, actor, timeProvider.GetUtcNow().UtcDateTime, fingerprint);
        for (var i = 0; i < MaxAttempts; i++)
        {
            var next = await attempt(context, cancellationToken);
            if (next.Refusal is { } refusal)
                return MovementResult.Refused(refusal);

            switch (await stock.SaveAsync(next.Write!, cancellationToken))
            {
                case SaveOutcome.Saved:
                    return MovementResult.Recorded(next.Write!.Movements);
                case SaveOutcome.MovementExists:
                    // A concurrent request with the same key won the race.
                    return await ReplayAsync(productId, movementIds, fingerprint, cancellationToken)
                        ?? throw new InvalidOperationException($"Movement {key} exists but could not be read back.");
                default:
                    continue;
            }
        }

        return MovementResult.Refused(StockRefusal.TransactionConflict());
    }

    private async Task<MovementResult?> ReplayAsync(
        string productId, IReadOnlyList<string> movementIds, string fingerprint, CancellationToken cancellationToken)
    {
        var existing = await stock.FindMovementsAsync(productId, movementIds, cancellationToken);
        if (existing.Count == 0)
            return null;

        return existing.All(movement => movement.PayloadHash == fingerprint)
            ? MovementResult.Recorded(existing)
            : MovementResult.Refused(StockRefusal.IdempotencyKeyReused());
    }
}
