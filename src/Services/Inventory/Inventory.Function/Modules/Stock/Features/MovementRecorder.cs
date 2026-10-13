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
        if (await ReplayAsync(productId, key, movementIds, fingerprint, cancellationToken) is { } replay)
            return replay;

        for (var i = 0; i < MaxAttempts; i++)
        {
            // Stamped per attempt, so a movement that commits after a retry sorts after the ones it
            // waited for and the history keeps reading as a running sum.
            var context = new MovementContext(key, actor, timeProvider.GetUtcNow().UtcDateTime, fingerprint);
            var next = await attempt(context, cancellationToken);
            if (next.Refusal is { } refusal)
                return MovementResult.Refused(refusal);

            var outcome = await stock.SaveAsync(next.Write!, cancellationToken);
            if (outcome == SaveOutcome.Saved)
                return MovementResult.Recorded(next.Write!.Movements);

            // Whatever stopped the write — DynamoDB may report a twin request's commit as a
            // conflict rather than a failed condition — the movement may now be recorded: answer it
            // as the replay instead of judging it again against the stock it changed.
            if (await ReplayAsync(productId, key, movementIds, fingerprint, cancellationToken) is { } raced)
                return raced;
            if (outcome == SaveOutcome.MovementExists)
                throw new InvalidOperationException($"Movement {key} exists but could not be read back.");
        }

        return MovementResult.Refused(StockRefusal.TransactionConflict());
    }

    // Every id the key could have produced, of any kind, so a key already used for another kind of
    // movement on this product is caught too. The key is scoped to the product (ADR-0052 §7).
    private static IReadOnlyList<string> AnyIdsOf(Guid key) => [key.ToString(), $"{key}#OUT", $"{key}#IN"];

    private async Task<MovementResult?> ReplayAsync(
        string productId, Guid key, IReadOnlyList<string> movementIds, string fingerprint, CancellationToken cancellationToken)
    {
        var existing = await stock.FindMovementsAsync(productId, AnyIdsOf(key), cancellationToken);
        if (existing.Count == 0)
            return null;

        var found = existing.Select(movement => movement.MovementId).ToHashSet();
        if (!found.SetEquals(movementIds))
        {
            // A movement's rows are written in one transaction: part of them means something is wrong.
            if (found.Overlaps(movementIds))
                throw new InvalidOperationException($"Movement {key} has only some of its rows.");
            return MovementResult.Refused(StockRefusal.IdempotencyKeyReused());
        }

        return existing.All(movement => movement.PayloadHash == fingerprint)
            ? MovementResult.Recorded([.. movementIds.Select(id => existing.Single(movement => movement.MovementId == id))])
            : MovementResult.Refused(StockRefusal.IdempotencyKeyReused());
    }
}
