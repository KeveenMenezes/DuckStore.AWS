namespace Review.Function;

public partial class Functions
{
    // Triggered by the reviews DynamoDB Stream. Dispatches each record to the registered
    // IStreamRule<ReviewStreamImage> rules (ADR-0019): ReviewCreatedRule on INSERT, ReviewUpdatedRule
    // on MODIFY (ADR-0029) — so both a brand-new review and an upsert-edit of an existing one feed
    // CatalogView's rating aggregation.
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<ReviewStreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<ReviewStreamImage>(
                record.EventName,
                ReviewStreamImage.From(record.Dynamodb.OldImage),
                ReviewStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
