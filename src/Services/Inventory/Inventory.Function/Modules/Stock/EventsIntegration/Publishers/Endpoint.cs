using Inventory.Function.Modules.Stock.EventsIntegration.Publishers;

namespace Inventory.Function;

public partial class Functions
{
    // Triggered by the inventory-stock DynamoDB Stream (inventory-stock-stream-publisher). Dispatches
    // each record to ProductStockChangedRule (ADR-0019, ADR-0052 §8): every balance change republishes
    // the product's storefront total.
    [LambdaFunction]
    public async Task InventoryStockStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<StockStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<StockStreamImage>(
                record.EventName,
                StockStreamImage.From(record.Dynamodb.OldImage),
                StockStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
