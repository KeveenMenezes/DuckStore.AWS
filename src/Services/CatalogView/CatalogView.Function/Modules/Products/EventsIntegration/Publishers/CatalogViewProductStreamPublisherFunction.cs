namespace CatalogView.Function;

// DynamoDB Streams-triggered publisher: projects each catalogview-products record into a
// StreamContext (old + new image) and lets the registered IStreamRule<CatalogViewProductStreamImage>
// rules decide what to publish (ADR-0019/ADR-0031/ADR-0035). Runs after CatalogView's own write has
// committed, so the SPA revalidator (subscribed to these events, not the upstream ones) can never
// invalidate CloudFront before CatalogView's data is actually in place.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task CatalogViewProductStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<CatalogViewProductStreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<CatalogViewProductStreamImage>(
                record.EventName,
                CatalogViewProductStreamImage.From(record.Dynamodb.OldImage),
                CatalogViewProductStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
