using Amazon.DynamoDBv2;

namespace BuildingBlocks.Messaging.Idempotency;

public class DynamoIdempotentEventConsumer(IAmazonDynamoDB dynamoDb, string tableName)
    : IIdempotentEventConsumer
{
    public async Task ConsumeAsync(
        string eventId,
        IReadOnlyList<TransactWriteItem> businessItems,
        CancellationToken cancellationToken = default)
    {
        var items = new List<TransactWriteItem>
        {
            new()
            {
                Put = new Put
                {
                    TableName = tableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = new(eventId),
                        ["ProcessedOnUtc"] = new(DateTime.UtcNow.ToString("O"))
                    },
                    ConditionExpression = "attribute_not_exists(PK)"
                }
            }
        };
        items.AddRange(businessItems);

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = items },
                cancellationToken);
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons.Count > 0 &&
                ex.CancellationReasons[0].Code == "ConditionalCheckFailed")
        {
            // Event already processed — idempotent no-op.
        }
    }
}
