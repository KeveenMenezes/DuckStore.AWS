namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers;

// The order whose points redemption a payment or cancellation event settles. Shared by the
// PaymentAuthorized and OrderCancelled consumers, which differ only in the transition applied.
public sealed record SettleRedemptionCommand(OwnerId OwnerId, Guid OrderId);

public static class SettleRedemptionMapper
{
    // Both events carry the Cognito sub as CustomerId (checkout is Cognito-only), which is what
    // makes the REDEMPTION# row's base-table key known — see IPlayerProgressRepository.GetRedemptionAsync.
    // A malformed payload throws, so EventBridge retries it into the DLQ.
    public static SettleRedemptionCommand ToCommand(Guid customerId, Guid orderId)
    {
        var failures = new List<ValidationFailure>();
        if (customerId == Guid.Empty)
        {
            failures.Add(new("CustomerId", "CustomerId is required"));
        }

        if (orderId == Guid.Empty)
        {
            failures.Add(new("OrderId", "OrderId is required"));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new SettleRedemptionCommand(OwnerId.Of($"USER#{customerId}"), orderId);
    }
}
