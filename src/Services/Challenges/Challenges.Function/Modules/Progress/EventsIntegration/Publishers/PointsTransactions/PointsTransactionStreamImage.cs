namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions;

// The subset of a "points-transactions" ledger row the publisher rules reason about, projected from
// a DynamoDB Streams image (ADR-0048 §1). Credits and redemptions share the table; OrderId is only
// present on redemption rows (the sparse GSI1 key).
public sealed record PointsTransactionStreamImage(
    string OwnerId,
    string TransactionId,
    string Status,
    int Points,
    string? OrderId)
{
    public static PointsTransactionStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new PointsTransactionStreamImage(
            image.TryGetValue(PointsTransactionsSchema.PartitionKey, out var ownerId) ? ownerId.S : string.Empty,
            image.TryGetValue(PointsTransactionsSchema.SortKey, out var transactionId) ? transactionId.S : string.Empty,
            image.TryGetValue("Status", out var status) ? status.S : string.Empty,
            image.TryGetValue("Points", out var points) && !string.IsNullOrEmpty(points.N)
                ? int.Parse(points.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue(PointsTransactionsSchema.OrderIdAttribute, out var orderId) ? orderId.S : null);
    }
}
