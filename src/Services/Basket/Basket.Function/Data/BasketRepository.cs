using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Basket.Function.Data;

public class BasketRepository(IAmazonDynamoDB dynamoDb)
    : IBasketRepository
{
    public const string TableName = "ShoppingCarts";

    public async Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["UserName"] = new(userName) }
            },
            cancellationToken);

        return response.Item.Count == 0 ?
            throw new BasketNotFoundException(userName) :
            JsonSerializer.Deserialize<ShoppingCart>(response.Item["Data"].S)!;
    }

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        await dynamoDb.PutItemAsync(
            new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["UserName"] = new(cart.UserName),
                    ["Data"] = new(JsonSerializer.Serialize(cart))
                }
            },
            cancellationToken);

        return cart;
    }

    public Task DeleteBasket(string userName, CancellationToken cancellationToken) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["UserName"] = new(userName) }
            },
            cancellationToken);

    public Task MarkCheckoutAsync(string userName, string checkoutDataJson, CancellationToken cancellationToken) =>
        dynamoDb.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["UserName"] = new(userName) },
                UpdateExpression = "SET #T = :type, CheckoutData = :data",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#T"] = "Type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":type"] = new("Checkout"),
                    [":data"] = new(checkoutDataJson)
                }
            },
            cancellationToken);
}
