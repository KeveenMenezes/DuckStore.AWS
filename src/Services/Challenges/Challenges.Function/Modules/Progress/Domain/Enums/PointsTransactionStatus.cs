namespace Challenges.Function.Modules.Progress.Domain.Enums;

// Credits are born Completed and never leave it. The remaining values are the redemption state
// machine of ADR-0048 §5, declared now so the persisted and GraphQL vocabulary is stable; nothing
// transitions into them until cart-points-redemption lands.
public enum PointsTransactionStatus
{
    Completed,
    Reserved,
    Used,
    Released,
    Failed,
    Refunded
}
