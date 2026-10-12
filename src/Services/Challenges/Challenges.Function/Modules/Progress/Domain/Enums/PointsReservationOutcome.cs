namespace Challenges.Function.Modules.Progress.Domain.Enums;

// What a checkout's points reservation ended up writing (ADR-0048 §5).
public enum PointsReservationOutcome
{
    // Debited; REDEMPTION#<orderId> written Reserved.
    Reserved,

    // The balance was short; nothing debited, REDEMPTION#<orderId> written Failed.
    Failed,

    // REDEMPTION#<orderId> already existed — a redelivered checkout. Nothing written.
    AlreadyProcessed
}
