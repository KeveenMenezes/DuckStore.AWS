namespace Payment.Function.Modules.Payments.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Declined = 3,

    // The checkout used points: the gateway is not asked until Challenges reserves them
    // (PointsReserved → Pending) or fails to (PointsReservationFailed → Declined) — ADR-0048 §6.
    // Persisted by name; appended so the existing values keep their numbers.
    AwaitingPoints = 4
}
