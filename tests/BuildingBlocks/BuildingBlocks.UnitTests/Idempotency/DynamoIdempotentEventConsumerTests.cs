using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BuildingBlocks.Messaging.Idempotency;

namespace BuildingBlocks.UnitTests.Idempotency;

public class DynamoIdempotentEventConsumerTests
{
    private const string TableName = "processed-events";

    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoIdempotentEventConsumer _consumer;

    public DynamoIdempotentEventConsumerTests()
    {
        _consumer = new DynamoIdempotentEventConsumer(_dynamoDb.Object, TableName);
    }

    [Fact]
    public async Task ConsumeAsync_WritesIdempotencyMarkerWithBusinessItems_OnFirstTimeProcessing()
    {
        var businessItem = new TransactWriteItem
        {
            Put = new Put { TableName = "orders", Item = new Dictionary<string, AttributeValue>() }
        };

        TransactWriteItemsRequest? capturedRequest = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _consumer.ConsumeAsync("event-1", [businessItem]);

        Assert.NotNull(capturedRequest);
        Assert.Equal(2, capturedRequest!.TransactItems.Count);

        var marker = capturedRequest.TransactItems[0].Put;
        Assert.Equal(TableName, marker.TableName);
        Assert.Equal("event-1", marker.Item["PK"].S);
        Assert.Equal("attribute_not_exists(PK)", marker.ConditionExpression);

        Assert.Same(businessItem, capturedRequest.TransactItems[1]);
    }

    [Fact]
    public async Task ConsumeAsync_IsNoOp_WhenEventAlreadyProcessed()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("Transaction cancelled")
            {
                CancellationReasons = [new CancellationReason { Code = "ConditionalCheckFailed" }]
            });

        var exception = await Record.ExceptionAsync(() => _consumer.ConsumeAsync("event-1", []));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ConsumeAsync_Propagates_WhenTransactionCancelledForAnotherReason()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("Transaction cancelled")
            {
                CancellationReasons = [new CancellationReason { Code = "ProvisionedThroughputExceeded" }]
            });

        await Assert.ThrowsAsync<TransactionCanceledException>(
            () => _consumer.ConsumeAsync("event-1", []));
    }

    [Fact]
    public async Task ConsumeAsync_Propagates_WhenDynamoDbThrowsUnrelatedException()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(
                It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("boom"));

        await Assert.ThrowsAsync<AmazonDynamoDBException>(
            () => _consumer.ConsumeAsync("event-1", []));
    }
}
