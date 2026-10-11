namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.BasketCheckout;

// The points a checkout asked to use on its order.
public sealed record ReservePointsCommand(OwnerId OwnerId, Guid OrderId, int Points);

public sealed class BasketCheckoutHandler(IPlayerProgressRepository repository)
{
    // Pricing already capped the points (ADR-0048 §3); the balance is checked here, by the
    // database, in the same transaction that debits it. Either outcome is written to the ledger,
    // and its stream tells Payment whether to go on (PointsReserved) or decline (PointsReservationFailed).
    public Task<PointsReservationOutcome> HandleAsync(
        ReservePointsCommand command, CancellationToken cancellationToken = default) =>
        repository.ReservePointsAsync(command.OwnerId, command.OrderId, command.Points, cancellationToken);
}
