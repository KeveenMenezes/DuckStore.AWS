namespace Pricing.Function.Modules.Campaigns.Data;

// A Campaign is one item in "campaigns" (PK=Id). Creating/cancelling it also fans out to
// "product-discounts" (PK=ProductId) — one denormalized row per enrolled product — so reads of
// "what's the current discount for product X" stay a plain GetItem (an AppSync direct resolver),
// with expiry checked at read time against StartsAt/EndsAt.
//
// The fan-out is a plain in-handler TransactWriteItems, not a DynamoDB Streams rule (ADR-0019's
// rule pattern is for cross-service CDC; this is internal to Pricing, so no Streams are enabled on
// "campaigns"). TransactWriteItems caps at 100 items, so a campaign is limited to 99 products —
// acceptable for this demo, not enforced in code.
public class DynamoCampaignRepository(IAmazonDynamoDB dynamoDb) : ICampaignRepository
{
    public const string TableName = "campaigns";
    public const string ProductDiscountsTableName = "product-discounts";

    // Used by the ProductDeleted consumer to retract a stale product-discounts row when its
    // product is deleted in Catalog — a no-op if the product wasn't enrolled in a campaign.
    internal static TransactWriteItem ToDeleteProductDiscountTransactWriteItem(Guid productId) =>
        new()
        {
            Delete = new Delete
            {
                TableName = ProductDiscountsTableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.ToString()) }
            }
        };

    public async Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapCampaign(response.Item) : null;
    }

    public Task AddAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        var items = new List<TransactWriteItem>
        {
            new() { Put = new Put { TableName = TableName, Item = ToItem(campaign) } }
        };

        items.AddRange(campaign.ProductIds.Select(productId =>
            new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = ProductDiscountsTableName,
                    Item = ToProductDiscountItem(campaign, productId.Value)
                }
            }));

        return dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest { TransactItems = items }, cancellationToken);
    }

    public Task CancelAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        var items = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue> { ["Id"] = new(campaign.Id.Value.ToString()) },
                    UpdateExpression = "SET #status = :status",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new("Cancelled")
                    }
                }
            }
        };

        items.AddRange(campaign.ProductIds.Select(productId =>
            new TransactWriteItem
            {
                Delete = new Delete
                {
                    TableName = ProductDiscountsTableName,
                    Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.Value.ToString()) }
                }
            }));

        return dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest { TransactItems = items }, cancellationToken);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    public async Task<ActiveDiscount?> GetActiveDiscountForProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = ProductDiscountsTableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.ToString()) }
            },
            cancellationToken);

        if (response.Item is not { Count: > 0 } item)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var startsAt = DateTime.Parse(item["StartsAt"].S, null, DateTimeStyles.RoundtripKind);
        var endsAt = DateTime.Parse(item["EndsAt"].S, null, DateTimeStyles.RoundtripKind);

        if (now < startsAt || now > endsAt)
        {
            return null;
        }

        return new ActiveDiscount(
            Enum.Parse<DiscountType>(item["DiscountType"].S),
            decimal.Parse(item["Value"].N, CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, AttributeValue> ToItem(Campaign campaign) =>
        new()
        {
            ["Id"] = new(campaign.Id.Value.ToString()),
            ["Name"] = new(campaign.Name),
            ["DiscountType"] = new(campaign.Discount.Type.ToString()),
            ["Value"] = new AttributeValue { N = campaign.Discount.Amount.ToString(CultureInfo.InvariantCulture) },
            ["StartsAt"] = new(campaign.StartsAt.ToString("o")),
            ["EndsAt"] = new(campaign.EndsAt.ToString("o")),
            ["ProductIds"] = new AttributeValue
            {
                SS = [.. campaign.ProductIds.Select(id => id.Value.ToString())]
            },
            ["Status"] = new(campaign.IsCancelled ? "Cancelled" : "Active"),
            ["CreatedAt"] = new((campaign.CreatedAt ?? DateTime.UtcNow).ToString("o"))
        };

    private static Dictionary<string, AttributeValue> ToProductDiscountItem(Campaign campaign, Guid productId) =>
        new()
        {
            ["ProductId"] = new(productId.ToString()),
            ["CampaignId"] = new(campaign.Id.Value.ToString()),
            ["DiscountType"] = new(campaign.Discount.Type.ToString()),
            ["Value"] = new AttributeValue { N = campaign.Discount.Amount.ToString(CultureInfo.InvariantCulture) },
            ["StartsAt"] = new(campaign.StartsAt.ToString("o")),
            ["EndsAt"] = new(campaign.EndsAt.ToString("o")),
            // DynamoDB TTL attribute (ADR-0044). Expiry stays a read-time check for correctness
            // (ADR-0026 §7 — no scheduler), but the delete TTL performs also lands on this table's
            // stream, which is what tells CatalogView to drop back to the undiscounted price. TTL
            // is best-effort and can lag hours past EndsAt; readers are already immune, only the
            // denormalized catalog copy waits.
            ["ExpiresAt"] = new AttributeValue { N = ToUnixSeconds(campaign.EndsAt).ToString(CultureInfo.InvariantCulture) }
        };

    private static long ToUnixSeconds(DateTime value) =>
        new DateTimeOffset(value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime()).ToUnixTimeSeconds();

    private static Campaign MapCampaign(Dictionary<string, AttributeValue> item) =>
        Campaign.Load(
            Guid.Parse(item["Id"].S),
            item["Name"].S,
            Enum.Parse<DiscountType>(item["DiscountType"].S),
            decimal.Parse(item["Value"].N, CultureInfo.InvariantCulture),
            DateTime.Parse(item["StartsAt"].S, null, DateTimeStyles.RoundtripKind),
            DateTime.Parse(item["EndsAt"].S, null, DateTimeStyles.RoundtripKind),
            item["Status"].S == "Cancelled",
            (item.TryGetValue("ProductIds", out var ids) ? ids.SS : []).Select(Guid.Parse),
            DateTime.Parse(item["CreatedAt"].S, null, DateTimeStyles.RoundtripKind));
}
