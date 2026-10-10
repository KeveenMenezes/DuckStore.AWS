namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.OrderCancelled;

public static class OrderCancelledMapper
{
    public static SettleRedemptionCommand ToCommand(OrderCancelledEvent message) =>
        SettleRedemptionMapper.ToCommand(message.CustomerId, message.OrderId);
}

public sealed class OrderCancelledHandler(IPlayerProgressRepository repository)
{
    // Reserved → Released or Used → Refunded, giving the points back in the same transaction
    // (ADR-0048 §5). False when there was nothing to give back: no points on the order, a Failed
    // reservation (nothing was debited), or the row already settled (a duplicate).
    public async Task<bool> HandleAsync(SettleRedemptionCommand command, CancellationToken cancellationToken = default)
    {
        var redemption = await repository.GetRedemptionAsync(command.OwnerId, command.OrderId, cancellationToken);

        return redemption is not null
            && redemption.CancelOrder(DateTime.UtcNow)
            && await repository.SaveSettlementAsync(redemption, cancellationToken);
    }
}
