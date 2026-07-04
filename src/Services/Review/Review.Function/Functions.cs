namespace Review.Function;

public partial class Functions
{
    // Triggered by the reviews DynamoDB Stream. For each INSERT it publishes a ReviewCreated
    // integration event to EventBridge so the Catalog service can fold the rating into the
    // product's AverageRating/RatingCount (CDC — ADR-0005/0008/0011). Only INSERTs matter:
    // a new review is the only thing that changes a product's aggregate rating here.
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewCreatedPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] IEventPublisher eventPublisher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            if (record.EventName != "INSERT")
                continue;

            var review = ReviewStreamImage.From(record.Dynamodb.NewImage);

            await eventPublisher.PublishAsync(new ReviewCreatedEvent
            {
                ReviewId = review.Id,
                ProductId = review.ProductId,
                Rating = review.Rating
            });
        }
    }
}
