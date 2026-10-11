namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    // Null for a checkout without points — every checkout reaches this consumer, and most are none
    // of its business. A checkout with points but a malformed payload throws, so EventBridge
    // retries it into the DLQ instead of the points silently never being reserved.
    public static ReservePointsCommand? ToCommand(BasketCheckoutEvent message)
    {
        if (message.PointsToUse <= 0)
        {
            return null;
        }

        if (message.OrderId == Guid.Empty)
        {
            throw new ValidationException([new(nameof(message.OrderId), "OrderId is required")]);
        }

        // Basket's OwnerId is already USER#<sub> for a checkout (Cognito-only); OwnerId.Of rejects
        // anything else, a guest cart included.
        return new ReservePointsCommand(OwnerId.Of(message.OwnerId), message.OrderId, message.PointsToUse);
    }
}
