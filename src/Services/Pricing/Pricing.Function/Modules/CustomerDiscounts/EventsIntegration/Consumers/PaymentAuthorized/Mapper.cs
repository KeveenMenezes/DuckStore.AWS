namespace Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PaymentAuthorized;

public static class PaymentAuthorizedMapper
{
    // Rebuilds the same "USER#<sub>" OwnerId customer-discounts is keyed by — checkout (and
    // therefore PaymentAuthorizedEvent.CustomerId) is Cognito-only, so this always matches the
    // OwnerId the discount was issued to (ADR-0046 §6).
    public static string ToOwnerId(Guid customerId) => $"USER#{customerId}";
}
