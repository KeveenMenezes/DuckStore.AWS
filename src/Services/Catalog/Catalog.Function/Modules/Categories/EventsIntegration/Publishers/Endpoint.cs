namespace Catalog.Function;

// Triggered by DynamoDB Streams on the categories table. Fires only on a rename — publishes
// CatalogCategorySyncEvent so CatalogView can rewrite the denormalized category name on every
// product document that references it.
public partial class Functions
{
    [LambdaFunction]
    public async Task CategoryStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<CategoryStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<CategoryStreamImage>(
                record.EventName,
                CategoryStreamImage.From(record.Dynamodb.OldImage),
                CategoryStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
