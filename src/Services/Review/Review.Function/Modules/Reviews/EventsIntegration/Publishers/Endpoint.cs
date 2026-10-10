namespace Review.Function;

public partial class Functions
{
    // Triggered by the reviews DynamoDB Stream. Dispatches each record to the registered
    // IStreamRule<ReviewStreamImage> rules (ADR-0019), which decide by status transition
    // (ADR-0049 §5): ReviewCreatedRule on → Published, ReviewUpdatedRule on Published → Published,
    // ReviewDeletedRule on Published → Deleted. Eligible inserts and TTL removes publish nothing.
    [LambdaFunction]
    public async Task ReviewStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<ReviewStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<ReviewStreamImage>(
                record.EventName,
                ReviewStreamImage.From(record.Dynamodb.OldImage),
                ReviewStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
