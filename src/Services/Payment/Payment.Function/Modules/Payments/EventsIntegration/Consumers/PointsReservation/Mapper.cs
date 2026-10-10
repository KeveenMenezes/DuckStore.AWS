namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PointsReservation;

public static class PointsReservationMapper
{
    public static ApplyPointsReservationCommand ToCommand(PointsReservedEvent message) =>
        new(message.OrderId, Reserved: true);

    public static ApplyPointsReservationCommand ToCommand(PointsReservationFailedEvent message) =>
        new(message.OrderId, Reserved: false);
}
