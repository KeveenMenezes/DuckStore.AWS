namespace Challenges.Function;

public partial class Functions
{
    // Triggered by the challenge-progress DynamoDB Stream. Dispatches each record to the
    // registered IStreamRule<ProgressStreamImage> rules (ADR-0019, ADR-0045 §9): ChallengeAnsweredRule
    // on an ATTEMPT# row's answered transition. A write to PROFILE never matches it.
    [LambdaFunction]
    public async Task ChallengesProgressStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<ProgressStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<ProgressStreamImage>(
                record.EventName,
                ProgressStreamImage.From(record.Dynamodb.OldImage),
                ProgressStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
