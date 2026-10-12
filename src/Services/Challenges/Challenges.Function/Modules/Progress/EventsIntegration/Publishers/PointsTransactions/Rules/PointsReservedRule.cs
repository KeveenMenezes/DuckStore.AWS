namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions.Rules;

// Fires on the INSERT of a Reserved redemption — the checkout's points were debited, so Payment may
// go on to the gateway (ADR-0048 §5). The row is born Reserved exactly once (attribute_not_exists),
// and settlement only MODIFYs it, so a plain INSERT match fires once per order. A point quantity,
// never money (ADR-0046 §1) — adding an amount here is NOT ALLOWED.
public sealed class PointsReservedRule : IStreamRule<PointsTransactionStreamImage>
{
    private static readonly string Reserved = nameof(PointsTransactionStatus.Reserved);

    public bool Match(StreamContext<PointsTransactionStreamImage> context) =>
        context.EventName == "INSERT"
        && context.New is { OrderId: not null } row
        && row.Status == Reserved;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<PointsTransactionStreamImage> context, CancellationToken cancellationToken = default)
    {
        var row = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(PointsReservedEvent),
            new PointsReservedEvent
            {
                OrderId = Guid.Parse(row.OrderId!),
                OwnerId = row.OwnerId,
                // Stored negative (it left the balance); published as the quantity reserved.
                Points = Math.Abs(row.Points)
            }));
    }
}
