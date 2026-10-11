namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.PaymentAuthorized;

public static class PaymentAuthorizedMapper
{
    public static SettleRedemptionCommand ToCommand(PaymentAuthorizedEvent message) =>
        SettleRedemptionMapper.ToCommand(message.CustomerId, message.OrderId);
}

public sealed class PaymentAuthorizedHandler(IPlayerProgressRepository repository)
{
    // Reserved → Used (ADR-0048 §5). False when there was nothing to do: the order used no points
    // (no row — most orders), or the row already left Reserved (a duplicate).
    public async Task<bool> HandleAsync(SettleRedemptionCommand command, CancellationToken cancellationToken = default)
    {
        var redemption = await repository.GetRedemptionAsync(command.OwnerId, command.OrderId, cancellationToken);

        return redemption is not null
            && redemption.Use(DateTime.UtcNow)
            && await repository.SaveSettlementAsync(redemption, cancellationToken);
    }
}
