using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;

namespace Inventory.Function;

public partial class Functions
{
    // Triggered by the inventory-warehouses DynamoDB Stream (inventory-warehouses-stream-publisher).
    // Republishes the storefront total of every product in a warehouse whose Active flag changed
    // (ADR-0052 §8).
    [LambdaFunction]
    public async Task InventoryWarehousesStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] WarehouseActivationPublisher publisher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<WarehouseStreamImage>(
                record.EventName,
                WarehouseStreamImage.From(record.Dynamodb.OldImage),
                WarehouseStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await publisher.PublishAsync(contexts, CancellationToken.None);
    }
}
