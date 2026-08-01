namespace Pricing.Function.Modules.CustomerDiscounts.Data;

// PK OwnerId, SK DiscountId (ADR-0046 §4) — a shape distinct from product-scoped discounts, which
// key by ProductId alone. TTL on ExpiresAt only clears dead rows eventually; expiry is checked at
// read time and is authoritative (ADR-0044 precedent), so nothing here depends on TTL firing.
public class DynamoCustomerDiscountRepository(IAmazonDynamoDB dynamoDb) : ICustomerDiscountRepository
{
    public const string TableName = CustomerDiscountsSchema.TableName;

    public async Task<CustomerDiscount?> GetAsync(
        string ownerId, string discountId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest { TableName = TableName, Key = Key(ownerId, discountId) },
            cancellationToken);

        return response.Item is { Count: > 0 } item ? MapDiscount(item) : null;
    }

    // For the idempotent PointsRedeemed consumer (ADR-0046 §4) — a fresh Guid per Issue, so this
    // can only fail on an astronomically unlikely id collision, never a legitimate replay (the
    // inbox row bundled alongside it is what actually guards against replaying the event).
    internal static TransactWriteItem ToPutTransactWriteItem(CustomerDiscount discount) =>
        new()
        {
            Put = new Put
            {
                TableName = TableName,
                Item = ToItem(discount),
                ConditionExpression = "attribute_not_exists(DiscountId)"
            }
        };

    // For the idempotent PaymentAuthorized consumer (ADR-0046 §6) — the only guard on double-spend:
    // a second authorization trying to burn an already-Consumed (or nonexistent) discount fails this
    // condition and the whole transaction (including that event's own inbox row) rolls back, which
    // the shared idempotent consumer treats identically to a replay — a silent no-op, never an error.
    internal static TransactWriteItem ToConsumeTransactWriteItem(string ownerId, string discountId) =>
        new()
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(ownerId, discountId),
                UpdateExpression = "SET #status = :consumed",
                ConditionExpression = "#status = :issued",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":consumed"] = new(nameof(CustomerDiscountStatus.Consumed)),
                    [":issued"] = new(nameof(CustomerDiscountStatus.Issued))
                }
            }
        };

    private static Dictionary<string, AttributeValue> Key(string ownerId, string discountId) =>
        new()
        {
            ["OwnerId"] = new(ownerId),
            ["DiscountId"] = new(discountId)
        };

    private static Dictionary<string, AttributeValue> ToItem(CustomerDiscount discount) =>
        new()
        {
            ["OwnerId"] = new(discount.OwnerId),
            ["DiscountId"] = new(discount.Id),
            ["Amount"] = new AttributeValue { N = discount.Amount.ToString(CultureInfo.InvariantCulture) },
            ["Status"] = new(discount.Status.ToString()),
            ["ExpiresAt"] = new AttributeValue { N = ToUnixSeconds(discount.ExpiresAt).ToString(CultureInfo.InvariantCulture) },
            ["SourceRedemptionId"] = new(discount.SourceRedemptionId),
            ["CreatedAt"] = new((discount.CreatedAt ?? DateTime.UtcNow).ToString("O"))
        };

    private static CustomerDiscount MapDiscount(Dictionary<string, AttributeValue> item) =>
        CustomerDiscount.Load(
            item["DiscountId"].S,
            item["OwnerId"].S,
            decimal.Parse(item["Amount"].N, CultureInfo.InvariantCulture),
            Enum.Parse<CustomerDiscountStatus>(item["Status"].S),
            FromUnixSeconds(long.Parse(item["ExpiresAt"].N, CultureInfo.InvariantCulture)),
            item["SourceRedemptionId"].S,
            DateTime.Parse(item["CreatedAt"].S, null, DateTimeStyles.RoundtripKind));

    private static long ToUnixSeconds(DateTime value) =>
        new DateTimeOffset(value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToUnixTimeSeconds();

    private static DateTime FromUnixSeconds(long value) =>
        DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
}
