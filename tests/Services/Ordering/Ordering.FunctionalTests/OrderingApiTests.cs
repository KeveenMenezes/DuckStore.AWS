using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Ordering.FunctionalTests;

// Ordering has no HTTP API — these validate, end-to-end against the seeded DynamoDB Local in the
// running Aspire graph, the two operations the AppSync direct resolvers rely on (ADR-0009):
// the GSI1 "orders by customer" query and the "delete order" DeleteItem.
public class OrderingApiTests(OrderingApiFixture fixture) : IClassFixture<OrderingApiFixture>
{
    private readonly IAmazonDynamoDB _dynamoDb = fixture.DynamoDb;

    [Fact]
    public async Task OrdersByCustomer_QueriesGsi1_ReturnsSeededOrder()
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = OrderingApiFixture.OrderingTable,
            IndexName = "GSI1",
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"CUSTOMER#{OrderingApiFixture.SeededCustomerId}")
            },
            ScanIndexForward = false
        });

        Assert.NotEmpty(response.Items);
        Assert.Contains(response.Items, item => item["Id"].S == OrderingApiFixture.SeededOrderId);
    }

    [Fact]
    public async Task DeleteOrder_RemovesItemById()
    {
        // Self-contained: write a throwaway order, delete it by Id, confirm it's gone — mirrors the
        // admin deleteOrder DeleteItem resolver without disturbing the seeded data other tests read.
        var orderId = Guid.NewGuid().ToString();

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = OrderingApiFixture.OrderingTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(orderId),
                ["Type"] = new("Order")
            }
        });

        await _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = OrderingApiFixture.OrderingTable,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(orderId) }
        });

        var afterDelete = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = OrderingApiFixture.OrderingTable,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(orderId) }
        });

        Assert.False(afterDelete.IsItemSet);
    }
}
