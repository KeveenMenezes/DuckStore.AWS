using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Review;

public static class ReviewExtensions
{
    // Must match ReviewSchema.TableName in Review.Function (table created by the seeder).
    private const string ReviewsTableName = "reviews";

    public static IResourceBuilder<ProjectResource> AddReviewServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var reviewSeeder = builder.AddProject<Projects.Review_DevelopmentDataSeeder>("review-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // CDC publisher: reviews INSERT/MODIFY → DynamoDB Stream → ReviewCreated/ReviewUpdated on
        // EventBridge (ADR-0011/ADR-0029), dispatched via the rule-based StreamRuleDispatcher (ADR-0019).
        builder.AddAWSLambdaFunction<Projects.Review_Function>(
                "review-reviews-event-publisher",
                lambdaHandler: "Review.Function::Review.Function.Functions_ReviewStreamPublisher_Generated::ReviewStreamPublisher")
            .WaitForCompletion(reviewSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ReviewsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return reviewSeeder;
    }
}
