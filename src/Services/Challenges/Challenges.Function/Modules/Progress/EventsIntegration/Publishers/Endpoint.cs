namespace Challenges.Function;

public partial class Functions
{
    // Triggered by the challenge-progress DynamoDB Stream. Dispatches each record to the
    // registered IStreamRule<ProgressStreamImage> rules (ADR-0019, ADR-0045 §9): ChallengeAnsweredRule
    // on an ATTEMPT# row's answered transition, PointsRedeemedRule on a new REDEMPTION# row
    // (ADR-0046 §3, no consumer until CH-12). A write to PROFILE never matches either rule.
    [LambdaFunction]
    public async Task ChallengesProgressStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<ProgressStreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<ProgressStreamImage>(
                record.EventName,
                ProgressStreamImage.From(record.Dynamodb.OldImage),
                ProgressStreamImage.From(record.Dynamodb.NewImage));

            await dispatcher.DispatchAsync(context);
        }
    }
}
