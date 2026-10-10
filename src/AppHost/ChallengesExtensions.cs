using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.Challenges;

public static class ChallengesExtensions
{
    // Must match ProgressSchema.TableName in Challenges.Function (table created by the seeder).
    private const string ChallengeProgressTableName = "challenge-progress";

    // Must match PointsTransactionsSchema.TableName in Challenges.Function (created by the seeder).
    private const string PointsTransactionsTableName = "points-transactions";

    public static IResourceBuilder<ProjectResource> AddChallengesServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var challengesSeeder = builder.AddProject<Projects.Challenges_DevelopmentDataSeeder>("challenges-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // AppSync Mutation resolver (Lambda-backed per ADR-0009/ADR-0045 §8 — grading needs the
        // stored answer key and a multi-item transaction, both beyond a direct resolver).
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-submit-answer",
                lambdaHandler: LambdaHandler("Challenges.Function", "SubmitChallengeAnswer"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // AppSync Mutation resolver (Lambda-backed — the penalty must be committed before the hint
        // text is returned, ADR-0045 §6).
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-reveal-hint",
                lambdaHandler: LambdaHandler("Challenges.Function", "RevealChallengeHint"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // AppSync Mutation resolver (Lambda-backed — the balance debit is a conditional
        // TransactWriteItems, ADR-0046 §2).
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-redeem-points",
                lambdaHandler: LambdaHandler("Challenges.Function", "RedeemChallengePoints"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // CDC publisher: challenge-progress INSERT/MODIFY → DynamoDB Stream →
        // ChallengeAnsweredEvent/PointsRedeemedEvent on EventBridge (ADR-0045 §7/ADR-0046),
        // dispatched via the rule-based StreamRuleDispatcher (ADR-0019).
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-progress-stream-publisher",
                lambdaHandler: LambdaHandler("Challenges.Function", "ChallengesProgressStreamPublisher"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ChallengeProgressTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // Consumes Review's ReviewCreatedEvent and credits the review points once per product
        // (SPEC-review-points). Writes only; publishes nothing. Locally EventBridge is best-effort,
        // so the SPA's local GraphQL backend simulates the credit instead.
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-review-created-consumer",
                lambdaHandler: LambdaHandler("Challenges.Function", "ReviewCreatedConsumer"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // Consumes Basket's BasketCheckoutEvent and reserves the points the checkout used
        // (ADR-0048 §5). Writes only. Locally EventBridge is best-effort, so the SPA's local GraphQL
        // backend debits the points synchronously instead.
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-basket-checkout-consumer",
                lambdaHandler: LambdaHandler("Challenges.Function", "BasketCheckoutConsumer"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // CDC publisher: points-transactions INSERT → DynamoDB Stream → PointsReservedEvent /
        // PointsReservationFailedEvent on EventBridge (ADR-0048 §5), rule-based (ADR-0019).
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-points-transactions-stream-publisher",
                lambdaHandler: LambdaHandler("Challenges.Function", "ChallengesPointsTransactionsStreamPublisher"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(PointsTransactionsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // Settlement of the checkout's points (ADR-0048 §5): Reserved → Used on PaymentAuthorizedEvent,
        // Released/Refunded (points back) on OrderCancelledEvent. Writes only.
        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-payment-authorized-consumer",
                lambdaHandler: LambdaHandler("Challenges.Function", "PaymentAuthorizedConsumer"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Challenges_Function>(
                "challenges-order-cancelled-consumer",
                lambdaHandler: LambdaHandler("Challenges.Function", "OrderCancelledConsumer"))
            .WaitForCompletion(challengesSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return challengesSeeder;
    }
}
