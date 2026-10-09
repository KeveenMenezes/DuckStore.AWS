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
}
