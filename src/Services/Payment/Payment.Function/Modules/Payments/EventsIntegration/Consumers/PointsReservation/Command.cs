namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PointsReservation;

// Challenges' outcome for the points of the checkout that created OrderId's payment (ADR-0048 §6).
public record ApplyPointsReservationCommand(Guid OrderId, bool Reserved);
