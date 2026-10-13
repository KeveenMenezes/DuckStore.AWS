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

    // The status a settlement moved this row out of — the condition the write is made under, so a
    // duplicated or out-of-order event finds the row already moved and writes nothing. Null until
    // a transition happens.
    public PointsTransactionStatus? PreviousStatus { get; private set; }

    // What a settlement gives back to the balance: exactly what left, read from the row itself and
    // never from the event (ADR-0048 §5). Zero for every other state, Failed included — it never
    // debited anything.
    public int PointsToReturn =>
        Status is PointsTransactionStatus.Released or PointsTransactionStatus.Refunded && PreviousStatus is not null
            ? -Points
            : 0;

    private PointsTransaction() { }

    public static PointsTransaction Load(
        OwnerId ownerId,
        string id,
        PointsTransactionType type,
        PointsTransactionStatus status,
        int points,
        string sourceId,
        string? orderId,
        DateTime createdAt,
        DateTime updatedAt) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Type = type,
            Status = status,
            Points = points,
            SourceId = sourceId,
            OrderId = orderId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

    // Payment authorized: the reserved points are spent. Balance unchanged — they left at checkout.
    public bool Use(DateTime at) =>
        Transition(PointsTransactionStatus.Reserved, PointsTransactionStatus.Used, at);

    // Order cancelled: before payment the reservation is released, after it the points are
    // refunded — either way they go back. Failed, or already settled, has nothing to give back.
    public bool CancelOrder(DateTime at) => Status switch
    {
        PointsTransactionStatus.Reserved =>
            Transition(PointsTransactionStatus.Reserved, PointsTransactionStatus.Released, at),
        PointsTransactionStatus.Used =>
            Transition(PointsTransactionStatus.Used, PointsTransactionStatus.Refunded, at),
        _ => false
    };

    // Every redemption transition is from one exact status; anything else is a no-op, so a
    // duplicated or out-of-order event never debits or refunds twice (ADR-0048 §5).
    private bool Transition(PointsTransactionStatus from, PointsTransactionStatus to, DateTime at)
    {
        if (Type != PointsTransactionType.Redemption || Status != from)
        {
            return false;
        }

        PreviousStatus = from;
        Status = to;
        UpdatedAt = at.ToUniversalTime();
        return true;
    }

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
    // (ADR-0048). The amount is the fixed PointsPolicy.ReviewCredit.
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
