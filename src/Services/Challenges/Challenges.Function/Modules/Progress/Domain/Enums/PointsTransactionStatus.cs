namespace Challenges.Function.Modules.Progress.Domain.Enums;

// Credits are born Completed and never leave it. The remaining values are the redemption state
// machine of ADR-0048 §5.
public enum PointsTransactionStatus
{
    Completed,
    Reserved,
    Used,
    Released,
    Failed,
    Refunded
}
