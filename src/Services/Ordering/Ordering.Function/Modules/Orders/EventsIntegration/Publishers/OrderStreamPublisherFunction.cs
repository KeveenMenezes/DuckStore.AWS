namespace Ordering.Function;

// DynamoDB Streams-triggered publisher: projects each record into a StreamContext (old + new image)
// and lets the registered IStreamRule<OrderStreamImage> rules decide what to publish (ADR-0019).
// Gated by the OrderFulfillment feature flag. DI, per-invocation scoping and input deserialization
// come from the Amazon.Lambda.Annotations generator via [LambdaStartup] Startup.
public partial class Functions
{
    [LambdaFunction]
    public async Task OrderStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<OrderStreamImage> dispatcher,
        [FromServices] IConfiguration configuration)
    {
        if (!bool.TryParse(configuration["FeatureManagement:OrderFullfilment"], out var enabled) || !enabled)
            return;

        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<OrderStreamImage>(
                record.EventName,
                OrderStreamImage.From(record.Dynamodb.OldImage),
                OrderStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
