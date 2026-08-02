namespace Payment.Function;

// DynamoDB Streams-triggered publisher: projects each record into a StreamContext (old + new
// image) and lets the registered IStreamRule<PaymentStreamImage> rules decide what to publish
// (ADR-0019). No feature-flag gate needed (unlike Ordering's OrderFulfillment flag).
public partial class Functions
{
    [LambdaFunction]
    public async Task PaymentStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<PaymentStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<PaymentStreamImage>(
                record.EventName,
                PaymentStreamImage.From(record.Dynamodb.OldImage),
                PaymentStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
