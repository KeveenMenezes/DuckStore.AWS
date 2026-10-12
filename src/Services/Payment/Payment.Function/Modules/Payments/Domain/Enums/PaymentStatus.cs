namespace Payment.Function.Modules.Payments.Domain.Enums;

// Persisted by name; new values are appended so the existing ones keep their numbers.
public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Declined = 3,

    // The checkout used points: the gateway is not asked until Challenges reserves them
    // (PointsReserved → Pending) or fails to (PointsReservationFailed → Declined) — ADR-0048 §6.
    AwaitingPoints = 4,

    // Written by the checkout saga before the cart closes; never reaches the gateway. The checkout
    // event promotes it (→ Pending/AwaitingPoints), or the saga voids it when the cart changed
    // (→ Voided) — ADR-0050 §6.
    Registered = 5,
    Voided = 6
}
