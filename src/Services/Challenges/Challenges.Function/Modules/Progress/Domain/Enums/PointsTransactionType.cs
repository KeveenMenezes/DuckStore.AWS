namespace Challenges.Function.Modules.Progress.Domain.Enums;

// Persisted by name and mirrored 1:1 by the GraphQL PointsTransactionType enum — renaming a value
// is a data migration. One value per TransactionId prefix in PointsTransactionsSchema
// (ADR-0048 §1).
public enum PointsTransactionType
{
    ChallengeCredit,
    ReviewCredit,
    Redemption
}
