namespace Inventory.Function.Modules.Warehouses.Data;

public sealed class DynamoWarehouseReader(IAmazonDynamoDB dynamoDb) : IWarehouseReader
{
    public async Task<WarehouseSnapshot?> GetAsync(string warehouseId, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = WarehousesSchema.TableName,
            Key = new() { [WarehousesSchema.PartitionKey] = new AttributeValue { S = warehouseId } },
            ConsistentRead = true,
        }, cancellationToken);

        var item = response.Item;
        if (item is null || item.Count == 0
            || !item.TryGetValue(WarehousesSchema.TypeAttribute, out var type) || type.S != WarehousesSchema.WarehouseType)
        {
            return null;
        }

        return new WarehouseSnapshot(
            warehouseId,
            item[WarehousesSchema.CodeAttribute].S,
            item.TryGetValue(WarehousesSchema.ActiveAttribute, out var active) && active.BOOL == true);
    }
}
