using System.Globalization;

namespace Catalog.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;

// Triggered by the ReviewCreated integration event on EventBridge (CDC — ADR-0011). Folds the
// new rating into the product's aggregate. The count/sum increment and the idempotency marker
// are written in a single TransactWriteItems, so a redelivered event never double-counts; the
// AverageRating is then recomputed from the authoritative RatingSum/RatingCount (idempotent).
public sealed class ReviewCreatedHandler(IIdempotentEventConsumer consumer, IAmazonDynamoDB dynamoDb)
{
    public async Task HandleAsync(string eventId, ReviewCreatedEvent evt)
    {
        var productId = evt.ProductId.ToString();
        var rating = evt.Rating;

        // Step 1 — atomic + idempotent: record the event and accumulate the running sum/count.
        await consumer.ConsumeAsync(eventId, [IncrementRatingTotals(productId, rating)]);

        // Step 2 — materialize the average from the authoritative totals. Safe to repeat: a
        // duplicate event makes step 1 a no-op and this rewrites the same value.
        await RecomputeAverageAsync(productId);
    }

    private static TransactWriteItem IncrementRatingTotals(string productId, int rating) =>
        new()
        {
            Update = new Update
            {
                TableName = DynamoProductRepository.TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
                UpdateExpression = "ADD RatingCount :one, RatingSum :rating",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":one"] = new() { N = "1" },
                    [":rating"] = new() { N = rating.ToString(CultureInfo.InvariantCulture) }
                },
                ConditionExpression = "attribute_exists(Id)"
            }
        };

    private async Task RecomputeAverageAsync(string productId)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = DynamoProductRepository.TableName,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
            ProjectionExpression = "RatingSum, RatingCount"
        });

        if (response.Item is not { Count: > 0 })
            return;

        var sum = long.Parse(response.Item["RatingSum"].N, CultureInfo.InvariantCulture);
        var count = long.Parse(response.Item["RatingCount"].N, CultureInfo.InvariantCulture);

        await dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = DynamoProductRepository.TableName,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
            UpdateExpression = "SET AverageRating = :avg",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":avg"] = new() { N = CalculateAverage(sum, count).ToString(CultureInfo.InvariantCulture) }
            }
        });
    }

    public static double CalculateAverage(long ratingSum, long ratingCount) =>
        ratingCount == 0 ? 0 : (double)ratingSum / ratingCount;
}
