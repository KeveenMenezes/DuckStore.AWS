namespace Pricing.Function.Modules.Prices.Data;

// One item per product, keyed by ProductId — a single PutItem is atomic on its own, no
// TransactWriteItems needed. Streams (NEW_IMAGE) drive the PriceChanged CDC publisher so
// CatalogView keeps the search document's price in sync (ADR-0026/0027).
public class DynamoPriceRepository(IAmazonDynamoDB dynamoDb) : IPriceRepository
{
    public const string TableName = "prices";

    public async Task<Price?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapPrice(response.Item) : null;
    }

    public async Task<List<Price>> GetByProductIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var keys = productIds.Distinct()
            .Select(id => new Dictionary<string, AttributeValue> { ["ProductId"] = new(id.ToString()) })
            .ToList();

        if (keys.Count == 0)
            return [];

        // BatchGetItem caps at 100 keys per call.
        const int batchSize = 100;
        var items = new List<Dictionary<string, AttributeValue>>();

        foreach (var chunk in keys.Chunk(batchSize))
        {
            var requestItems = new Dictionary<string, KeysAndAttributes>
            {
                [TableName] = new() { Keys = [.. chunk] }
            };

            while (requestItems.Count > 0)
            {
                var response = await dynamoDb.BatchGetItemAsync(
                    new BatchGetItemRequest { RequestItems = requestItems }, cancellationToken);

                if (response.Responses.TryGetValue(TableName, out var batch))
                    items.AddRange(batch);

                requestItems = response.UnprocessedKeys is { Count: > 0 } ? response.UnprocessedKeys : [];
            }
        }

        return [.. items.Select(MapPrice)];
    }

    public Task PutAsync(Price price, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(price) },
            cancellationToken);

    public Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.ToString()) }
            },
            cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    internal static TransactWriteItem ToDeleteTransactWriteItem(Guid productId) =>
        new()
        {
            Delete = new Delete
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductId"] = new(productId.ToString()) }
            }
        };

    private static Dictionary<string, AttributeValue> ToItem(Price price) =>
        new()
        {
            ["ProductId"] = new(price.Id.Value.ToString()),
            ["NominalPrice"] = new AttributeValue
            {
                N = price.NominalPrice.ToString(CultureInfo.InvariantCulture)
            },
            ["Cost"] = new AttributeValue
            {
                N = price.Cost.ToString(CultureInfo.InvariantCulture)
            },
            ["UpdatedAt"] = new((price.LastModified ?? price.CreatedAt ?? DateTime.UtcNow).ToString("o"))
        };

    private static Price MapPrice(Dictionary<string, AttributeValue> item) =>
        Price.Load(
            Guid.Parse(item["ProductId"].S),
            decimal.Parse(item["NominalPrice"].N, CultureInfo.InvariantCulture),
            item.TryGetValue("Cost", out var cost) ? decimal.Parse(cost.N, CultureInfo.InvariantCulture) : 0m,
            DateTime.Parse(item["UpdatedAt"].S, null, DateTimeStyles.RoundtripKind));
}
