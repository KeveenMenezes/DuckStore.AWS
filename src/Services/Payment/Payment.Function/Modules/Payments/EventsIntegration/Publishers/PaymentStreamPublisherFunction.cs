namespace Payment.Function;

// DynamoDB Streams-triggered publisher: projects each record into a StreamContext (old + new
// image) and lets the registered IStreamRule<PaymentStreamImage> rules decide what to publish
// (ADR-0019). No feature-flag gate needed (unlike Ordering's OrderFulfillment flag).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task PaymentStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<PaymentStreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<PaymentStreamImage>(
                record.EventName,
                PaymentStreamImage.From(record.Dynamodb.OldImage),
                PaymentStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
