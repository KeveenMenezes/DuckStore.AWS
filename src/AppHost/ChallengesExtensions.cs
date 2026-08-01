using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.Challenges;

public static class ChallengesExtensions
{
    // Must match ProgressSchema.TableName in Challenges.Function (table created by the seeder).
    private const string ChallengeProgressTableName = "challenge-progress";

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

        return challengesSeeder;
    }
}
