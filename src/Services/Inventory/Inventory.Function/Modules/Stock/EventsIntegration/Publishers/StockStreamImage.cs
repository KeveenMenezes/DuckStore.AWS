namespace Inventory.Function.Modules.Stock.EventsIntegration.Publishers;

// The keys of an inventory-stock row from a DynamoDB Streams image: which product's total to recompute.
public sealed record StockStreamImage(string ProductId, string WarehouseId)
{
    public static StockStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image) =>
        image is null || image.Count == 0
            ? null
            : new StockStreamImage(
                image.TryGetValue(StockSchema.PartitionKey, out var productId) ? productId.S : string.Empty,
                image.TryGetValue(StockSchema.SortKey, out var warehouseId) ? warehouseId.S : string.Empty);
}
