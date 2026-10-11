namespace Challenges.Function.Modules.Progress.Data;

// Single source of truth for the "points-transactions" ledger (ADR-0048 §1), shared by the
// repository, the development seeder and the AppHost stream wiring. Every change to a player's
// balance is one row here; the balance itself stays on challenge-progress PROFILE Score (§2).
//
// TransactionId is deterministic per source and written with attribute_not_exists — that IS the
// idempotency mechanism (one credit per question, one per reviewed product, one redemption per
// order). Never mint a random id for a row that has a natural source.
public static class PointsTransactionsSchema
{
    public const string TableName = "points-transactions";

    public const string PartitionKey = "OwnerId";
    public const string SortKey = "TransactionId";

    // LSI1 orders a player's history by date; the base table's SK is the deterministic id, which
    // sorts by type, not by time. An LSI can only be created with the table, so it ships now.
    public const string Lsi1Name = "LSI1";
    public const string CreatedAtAttribute = "CreatedAt";

    // GSI1 is sparse: only redemption rows carry OrderId, so payment/order events (which know the
    // OrderId but, for PaymentDeclinedEvent, not the owner) can find their row. Eventually
    // consistent — a lookup miss must be retried, never treated as "nothing to do" (ADR-0048 §5).
    public const string Gsi1Name = "GSI1";
    public const string OrderIdAttribute = "OrderId";

    private const string ChallengePrefix = "CHALLENGE#";
    private const string ReviewPrefix = "REVIEW#";
    private const string RedemptionPrefix = "REDEMPTION#";

    public static string ChallengeTransactionId(string questionId) => $"{ChallengePrefix}{questionId}";

    public static string ReviewTransactionId(Guid productId) => $"{ReviewPrefix}{productId}";

    public static string RedemptionTransactionId(Guid orderId) => $"{RedemptionPrefix}{orderId}";

    public static string ParseSourceId(string transactionId)
    {
        foreach (var prefix in (ReadOnlySpan<string>)[ChallengePrefix, ReviewPrefix, RedemptionPrefix])
        {
            if (transactionId.StartsWith(prefix, StringComparison.Ordinal))
                return transactionId[prefix.Length..];
        }

        throw new ArgumentException($"Unknown points transaction id \"{transactionId}\".", nameof(transactionId));
    }
}
