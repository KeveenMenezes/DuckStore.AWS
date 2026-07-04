namespace Basket.Function;

// DynamoDB Streams-triggered publisher: projects each record into a StreamContext (old + new
// image) and lets the registered IStreamRule<ShoppingCartStreamImage> rules decide what to
// publish (ADR-0019). DI, per-invocation scoping and input deserialization come from the
// Amazon.Lambda.Annotations generator via [LambdaStartup] Startup.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ShoppingCartStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<ShoppingCartStreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<ShoppingCartStreamImage>(
                record.EventName,
                ShoppingCartStreamImage.From(record.Dynamodb.OldImage),
                ShoppingCartStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
