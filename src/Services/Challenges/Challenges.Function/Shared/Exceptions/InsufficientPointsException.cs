namespace Challenges.Function.Shared.Exceptions;

// Thrown when the database's own Score >= :points condition fails on redemption (ADR-0046 §2) —
// the only guard on the balance invariant. Never a retry: the caller must ask for fewer points.
public class InsufficientPointsException(int points)
    : DomainException(nameof(PlayerProgress.Score), points, "insufficient points balance for this redemption");
