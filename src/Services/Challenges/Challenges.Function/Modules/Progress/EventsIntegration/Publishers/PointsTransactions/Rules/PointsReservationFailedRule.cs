namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions.Rules;

// Fires on the INSERT of a Failed redemption — the balance was short and nothing was debited, so
// Payment declines the payment with InsufficientPoints and Ordering cancels the order
// (ADR-0048 §5). Points is what the checkout asked for. A point quantity, never money
// (ADR-0046 §1).
public sealed class PointsReservationFailedRule : IStreamRule<PointsTransactionStreamImage>
{
    private static readonly string Failed = nameof(PointsTransactionStatus.Failed);

    public bool Match(StreamContext<PointsTransactionStreamImage> context) =>
        context.EventName == "INSERT"
        && context.New is { OrderId: not null } row
        && row.Status == Failed;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<PointsTransactionStreamImage> context, CancellationToken cancellationToken = default)
    {
        var row = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(PointsReservationFailedEvent),
            new PointsReservationFailedEvent
            {
                OrderId = Guid.Parse(row.OrderId!),
                OwnerId = row.OwnerId,
                Points = Math.Abs(row.Points)
            }));
    }
}
