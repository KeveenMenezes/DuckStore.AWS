namespace Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;

// What the warehouses publisher reasons about in an inventory-warehouses image: which item it is (a
// warehouse or a CODE# guard) and whether it is active.
public sealed record WarehouseStreamImage(string WarehouseId, string Type, bool Active)
{
    public static WarehouseStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image) =>
        image is null || image.Count == 0
            ? null
            : new WarehouseStreamImage(
                image.TryGetValue(WarehousesSchema.PartitionKey, out var id) ? id.S : string.Empty,
                image.TryGetValue(WarehousesSchema.TypeAttribute, out var type) ? type.S : string.Empty,
                image.TryGetValue(WarehousesSchema.ActiveAttribute, out var active) && active.BOOL == true);
}
