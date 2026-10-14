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
            balances.AddRange((page.Items ?? []).Select(item => new BalanceRow(
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
            products.AddRange((page.Items ?? []).Select(item => item[StockSchema.PartitionKey].S));
            startKey = page.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (startKey is not null);
        return products;
    }

    // BatchGetItem takes at most 100 keys per call and may leave some unprocessed under load; those are
    // asked for again, with a short backoff, until none remain. SDK v4 leaves absent maps null.
    private const int BatchGetLimit = 100;

    private async Task<IReadOnlyDictionary<string, WarehouseState>> WarehousesAsync(
        IEnumerable<string> warehouseIds, CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, WarehouseState>();
        foreach (var chunk in warehouseIds.Distinct().Chunk(BatchGetLimit))
        {
            var pending = chunk
                .Select(id => new Dictionary<string, AttributeValue> { [WarehousesSchema.PartitionKey] = new() { S = id } })
                .ToList();

            for (var attempt = 0; pending.Count > 0; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Min(attempt, 10)), cancellationToken);

                var response = await dynamoDb.BatchGetItemAsync(new BatchGetItemRequest
                {
                    RequestItems = new()
                    {
                        [WarehousesSchema.TableName] = new KeysAndAttributes { Keys = pending, ConsistentRead = true },
                    },
                }, cancellationToken);

                var items = response.Responses is { Count: > 0 } responses
                    && responses.TryGetValue(WarehousesSchema.TableName, out var found) ? found : [];
                foreach (var item in items)
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

                pending = response.UnprocessedKeys is { Count: > 0 } unprocessed
                    && unprocessed.TryGetValue(WarehousesSchema.TableName, out var left) && left.Keys is { Count: > 0 }
                        ? left.Keys
                        : [];
            }
        }

        return states;
    }

    // Rows written before the counter existed have none: they count as 0, and their next write makes it 1.
    private static long VersionOf(Dictionary<string, AttributeValue> item, string attribute) =>
        item.TryGetValue(attribute, out var version) && version.N is { } n
            ? long.Parse(n, CultureInfo.InvariantCulture)
            : 0;
}
