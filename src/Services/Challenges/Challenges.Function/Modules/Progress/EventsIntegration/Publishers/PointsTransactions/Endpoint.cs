using Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions;

namespace Challenges.Function;

public partial class Functions
{
    // Triggered by the points-transactions DynamoDB Stream (challenges-points-transactions-stream-
    // publisher). Dispatches each record to the IStreamRule<PointsTransactionStreamImage> rules
    // (ADR-0019, ADR-0048 §5): PointsReservedRule / PointsReservationFailedRule on a redemption
    // row's INSERT. Credits and settlement MODIFYs match neither and publish nothing.
    [LambdaFunction]
    public async Task ChallengesPointsTransactionsStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<PointsTransactionStreamImage> dispatcher)
    {
        var contexts = dynamoEvent.Records
            .Select(record => new StreamContext<PointsTransactionStreamImage>(
                record.EventName,
                PointsTransactionStreamImage.From(record.Dynamodb.OldImage),
                PointsTransactionStreamImage.From(record.Dynamodb.NewImage)))
            .ToList();

        await dispatcher.DispatchAsync(contexts);
    }
}
