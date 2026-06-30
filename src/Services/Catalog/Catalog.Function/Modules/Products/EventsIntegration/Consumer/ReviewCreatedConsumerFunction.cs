using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Function.Modules.Products.EventsIntegration.Consumer;

// Triggered by the ReviewCreated integration event on EventBridge (CDC — ADR-0011). Folds the
// new rating into the product's aggregate. The count/sum increment and the idempotency marker
// are written in a single TransactWriteItems, so a redelivered event never double-counts; the
// AverageRating is then recomputed from the authoritative RatingSum/RatingCount (idempotent).
public class ReviewCreatedConsumerFunction
{
    private readonly IServiceProvider _serviceProvider;

    public ReviewCreatedConsumerFunction()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(EventBridgeEvent<ReviewCreatedEvent> evt)
    {
        using var scope = _serviceProvider.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<IIdempotentEventConsumer>();
        var dynamoDb = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();

        var productId = evt.Detail.ProductId.ToString();
        var rating = evt.Detail.Rating;

        // Step 1 — atomic + idempotent: record the event and accumulate the running sum/count.
        await consumer.ConsumeAsync(evt.Id, [IncrementRatingTotals(productId, rating)]);

        // Step 2 — materialize the average from the authoritative totals. Safe to repeat: a
        // duplicate event makes step 1 a no-op and this rewrites the same value.
        await RecomputeAverageAsync(dynamoDb, productId);
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

    private static async Task RecomputeAverageAsync(IAmazonDynamoDB dynamoDb, string productId)
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

    // Pure, unit-testable: average rating from accumulated totals.
    public static double CalculateAverage(long ratingSum, long ratingCount) =>
        ratingCount == 0 ? 0 : (double)ratingSum / ratingCount;
}
