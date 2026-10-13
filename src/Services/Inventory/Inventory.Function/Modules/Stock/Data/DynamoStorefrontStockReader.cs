namespace Inventory.Function.Modules.Stock.Data;

public sealed class DynamoStorefrontStockReader(IAmazonDynamoDB dynamoDb) : IStorefrontStockReader
{
    public async Task<StorefrontTotal> TotalForAsync(string productId, CancellationToken cancellationToken)
    {
        var balances = new List<BalanceRow>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var page = await dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = StockSchema.TableName,
                KeyConditionExpression = "ProductId = :productId",
                ExpressionAttributeValues = new() { [":productId"] = new AttributeValue { S = productId } },
                ConsistentRead = true,
                ExclusiveStartKey = startKey,
            }, cancellationToken);
            balances.AddRange(page.Items.Select(item => new BalanceRow(
                item[StockSchema.SortKey].S,
                int.Parse(item[StockSchema.AvailableAttribute].N, CultureInfo.InvariantCulture),
                VersionOf(item, StockSchema.VersionAttribute))));
            startKey = page.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (startKey is not null);

        return StorefrontStock.TotalOf(balances, await WarehousesAsync(balances.Select(b => b.WarehouseId), cancellationToken));
    }

    public async Task<IReadOnlyList<string>> ProductsInWarehouseAsync(string warehouseId, CancellationToken cancellationToken)
    {
        var products = new List<string>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var page = await dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = StockSchema.TableName,
                IndexName = StockSchema.ByWarehouseIndex,
                KeyConditionExpression = "WarehouseId = :warehouseId",
                ExpressionAttributeValues = new() { [":warehouseId"] = new AttributeValue { S = warehouseId } },
                ProjectionExpression = "ProductId",
                ExclusiveStartKey = startKey,
            }, cancellationToken);
            products.AddRange(page.Items.Select(item => item[StockSchema.PartitionKey].S));
            startKey = page.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (startKey is not null);
        return products;
    }

    // BatchGetItem may leave keys unprocessed under load; they are asked for again until none remain.
    private async Task<IReadOnlyDictionary<string, WarehouseState>> WarehousesAsync(
        IEnumerable<string> warehouseIds, CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, WarehouseState>();
        var pending = warehouseIds.Distinct()
            .Select(id => new Dictionary<string, AttributeValue> { [WarehousesSchema.PartitionKey] = new() { S = id } })
            .ToList();

        while (pending.Count > 0)
        {
            var response = await dynamoDb.BatchGetItemAsync(new BatchGetItemRequest
            {
                RequestItems = new()
                {
                    [WarehousesSchema.TableName] = new KeysAndAttributes { Keys = pending, ConsistentRead = true },
                },
            }, cancellationToken);

            foreach (var item in response.Responses.GetValueOrDefault(WarehousesSchema.TableName) ?? [])
            {
                if (item.TryGetValue(WarehousesSchema.TypeAttribute, out var type) && type.S == WarehousesSchema.WarehouseType)
                {
                    var id = item[WarehousesSchema.PartitionKey].S;
                    states[id] = new WarehouseState(
                        id,
                        item.TryGetValue(WarehousesSchema.ActiveAttribute, out var active) && active.BOOL == true,
                        VersionOf(item, WarehousesSchema.VersionAttribute));
                }
            }

            pending = response.UnprocessedKeys.TryGetValue(WarehousesSchema.TableName, out var unprocessed)
                ? unprocessed.Keys
                : [];
        }

        return states;
    }

    // Rows written before the counter existed have none: they count as 0, and their next write makes it 1.
    private static long VersionOf(Dictionary<string, AttributeValue> item, string attribute) =>
        item.TryGetValue(attribute, out var version) && version.N is { } n
            ? long.Parse(n, CultureInfo.InvariantCulture)
            : 0;
}
