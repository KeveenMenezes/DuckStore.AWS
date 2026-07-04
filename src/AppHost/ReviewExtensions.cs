using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Review;

public static class ReviewExtensions
{
    // Must match ReviewSchema.TableName in Review.Function (table created by the seeder).
    private const string ReviewsTableName = "reviews";

    public static void AddReviewServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var reviewSeeder = builder.AddProject<Projects.Review_DevelopmentDataSeeder>("review-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // CDC publisher: reviews INSERT → DynamoDB Stream → publish ReviewCreated to EventBridge.
        builder.AddAWSLambdaFunction<Projects.Review_Function>(
                "review-reviews-event-publisher",
                lambdaHandler: "Review.Function::Review.Function.Functions_ReviewCreatedPublisher_Generated::ReviewCreatedPublisher")
            .WaitForCompletion(reviewSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ReviewsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");
    }
}
