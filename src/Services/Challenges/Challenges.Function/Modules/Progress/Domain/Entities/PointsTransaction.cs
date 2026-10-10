namespace Challenges.Function.Modules.Progress.Domain.Entities;

// One row of the points ledger (ADR-0048 §1). Id is the deterministic TransactionId of its
// source — written with attribute_not_exists, that IS the idempotency mechanism, so it is derived
// here from the source and never minted randomly. Points is signed: positive for credits,
// negative for redemptions. A row never carries a currency amount (ADR-0046 §1).
//
// An entity of the Progress module, not a module of its own: it succeeds the REDEMPTION# rows
// ADR-0045 §5 kept there, and every write to it moves PlayerProgress.Score (ADR-0048 §1).
public class PointsTransaction : Entity<string>
{
    public OwnerId OwnerId { get; private set; } = default!;
    public PointsTransactionType Type { get; private set; }
    public PointsTransactionStatus Status { get; private set; }
    public int Points { get; private set; }
    public string SourceId { get; private set; } = default!;
    public string? OrderId { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PointsTransaction() { }

    // Completed is terminal for a credit: the points were granted in the same TransactWriteItems
    // that wrote this row, so there is no intermediate state to model (ADR-0048 §2).
    public static PointsTransaction ChallengeCredit(OwnerId ownerId, QuestionId questionId, int points, DateTime at)
    {
        if (points <= 0)
        {
            throw new BadRequestException(nameof(points), points, "must be greater than zero");
        }

        // CreatedAt is the LSI1 sort key and Query.myPointsHistory reads it back as UTC.
        at = at.ToUniversalTime();

        return new PointsTransaction
        {
            Id = PointsTransactionsSchema.ChallengeTransactionId(questionId.Value),
            OwnerId = ownerId,
            Type = PointsTransactionType.ChallengeCredit,
            Status = PointsTransactionStatus.Completed,
            Points = points,
            SourceId = questionId.Value,
            CreatedAt = at,
            UpdatedAt = at
        };
    }

    // The checkout's points, debited and held until the payment settles (ADR-0048 §5). Keyed and
    // GSI1-indexed by the order: one redemption per order, found again by the payment/cancellation
    // consumers, which know the OrderId. Points is negative — it left the balance.
    public static PointsTransaction Reserve(OwnerId ownerId, Guid orderId, int points, DateTime at) =>
        Redemption(ownerId, orderId, points, PointsTransactionStatus.Reserved, at);

    // The checkout asked for more points than the balance held: nothing was debited. Points keeps
    // what was asked (negative, like any redemption) so the history can show it; Failed never
    // moves the balance in either direction.
    public static PointsTransaction Fail(OwnerId ownerId, Guid orderId, int points, DateTime at) =>
        Redemption(ownerId, orderId, points, PointsTransactionStatus.Failed, at);

    private static PointsTransaction Redemption(
        OwnerId ownerId, Guid orderId, int points, PointsTransactionStatus status, DateTime at)
    {
        if (points <= 0)
        {
            throw new BadRequestException(nameof(points), points, "must be greater than zero");
        }

        if (orderId == Guid.Empty)
        {
            throw new BadRequestException(nameof(orderId), orderId, "must not be empty");
        }

        at = at.ToUniversalTime();

        return new PointsTransaction
        {
            Id = PointsTransactionsSchema.RedemptionTransactionId(orderId),
            OwnerId = ownerId,
            Type = PointsTransactionType.Redemption,
            Status = status,
            Points = -points,
            SourceId = orderId.ToString(),
            OrderId = orderId.ToString(),
            CreatedAt = at,
            UpdatedAt = at
        };
    }

    // Keyed by product, not by review: the review row can go Published -> Deleted -> Published,
    // or expire by TTL and be bought and written again, and none of that may credit twice
    // (SPEC-review-points). The amount is the fixed PointsPolicy.ReviewCredit.
    public static PointsTransaction ReviewCredit(OwnerId ownerId, Guid productId, DateTime at)
    {
        if (productId == Guid.Empty)
        {
            throw new BadRequestException(nameof(productId), productId, "must not be empty");
        }

        at = at.ToUniversalTime();

        return new PointsTransaction
        {
            Id = PointsTransactionsSchema.ReviewTransactionId(productId),
            OwnerId = ownerId,
            Type = PointsTransactionType.ReviewCredit,
            Status = PointsTransactionStatus.Completed,
            Points = PointsPolicy.ReviewCredit,
            SourceId = productId.ToString(),
            CreatedAt = at,
            UpdatedAt = at
        };
    }
}
