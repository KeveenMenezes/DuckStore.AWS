namespace Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PointsRedeemed;

public static class PointsRedeemedHandler
{
    // The conversion (Amount) is decided here and nowhere else (ADR-0046 §1, §4) — the event
    // carries only a point quantity in, and this produces the one write that mints the discount.
    public static IReadOnlyList<TransactWriteItem> BuildIssueTransactItems(CustomerDiscount discount) =>
        [DynamoCustomerDiscountRepository.ToPutTransactWriteItem(discount)];
}
