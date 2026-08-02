namespace Catalog.Function;

public partial class Functions
{
    // Dispatches every products-table Streams record to whichever rules match — currently
    // ProductCreatedRule/ProductUpdatedRule/ProductDeletedRule (thin, id-only events) and
    // ProductSyncedRule (full payload for CatalogView), one rule per domain occurrence (ADR-0031).
    [LambdaFunction]
    public async Task ProductStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<CatalogStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<CatalogStreamImage>(
                record.EventName,
                CatalogStreamImage.From(record.Dynamodb.OldImage),
                CatalogStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
