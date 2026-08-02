namespace CatalogView.Function;

// DynamoDB Streams-triggered publisher: projects each catalogview-products record into a
// StreamContext (old + new image) and lets the registered IStreamRule<CatalogViewProductStreamImage>
// rules decide what to publish (ADR-0019/ADR-0031/ADR-0035). Runs after CatalogView's own write has
// committed, so the SPA revalidator (subscribed to these events, not the upstream ones) can never
// invalidate CloudFront before CatalogView's data is actually in place.
public partial class Functions
{
    [LambdaFunction]
    public async Task CatalogViewProductStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<CatalogViewProductStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<CatalogViewProductStreamImage>(
                record.EventName,
                CatalogViewProductStreamImage.From(record.Dynamodb.OldImage),
                CatalogViewProductStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
