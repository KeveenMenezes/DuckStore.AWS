namespace Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PaymentAuthorized;

public static class PaymentAuthorizedHandler
{
    public static IReadOnlyList<TransactWriteItem> BuildConsumeTransactItems(string ownerId, string discountId) =>
        [DynamoCustomerDiscountRepository.ToConsumeTransactWriteItem(ownerId, discountId)];
}
