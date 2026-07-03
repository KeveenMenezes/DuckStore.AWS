using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Basket.Function.Data;

public class BasketRepository(IAmazonDynamoDB dynamoDb)
    : IBasketRepository
{
    public const string TableName = "shopping-carts";

    // Sliding TTL window for guest carts; renewed on every write (see BuildItem).
    private static readonly TimeSpan GuestCartTtl = TimeSpan.FromDays(15);

    public async Task<ShoppingCart> GetBasket(string ownerId, CancellationToken cancellationToken) =>
        await TryGetBasket(ownerId, cancellationToken) ?? throw new BasketNotFoundException(ownerId);

    public async Task<ShoppingCart?> TryGetBasket(string ownerId, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(ownerId) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ?
            BasketSerializer.Deserialize(response.Item["Data"].S) :
            null;
    }

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        await dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = BuildItem(cart) },
            cancellationToken);

        return cart;
    }

    public Task DeleteBasket(string ownerId, CancellationToken cancellationToken) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(ownerId) }
            },
            cancellationToken);

    public Task MarkCheckoutAsync(string ownerId, string checkoutDataJson, CancellationToken cancellationToken) =>
        dynamoDb.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(ownerId) },
                UpdateExpression = "SET #T = :type, CheckoutData = :data",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#T"] = "Type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":type"] = new("Checkout"),
                    [":data"] = new(checkoutDataJson)
                }
            },
            cancellationToken);

    // Atomic merge: writes the merged user cart and removes the guest cart in one transaction.
    // The delete is conditional on the guest cart still existing, so a retry after a partial
    // failure (guest already deleted) fails the transaction and is treated as already-merged.
    public async Task MergeAsync(ShoppingCart mergedUserCart, string guestOwnerId, CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        new TransactWriteItem { Put = new Put { TableName = TableName, Item = BuildItem(mergedUserCart) } },
                        new TransactWriteItem
                        {
                            Delete = new Delete
                            {
                                TableName = TableName,
                                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(guestOwnerId) },
                                ConditionExpression = "attribute_exists(OwnerId)"
                            }
                        }
                    ]
                },
                cancellationToken);
        }
        catch (TransactionCanceledException)
        {
            // Guest cart already gone (concurrent/duplicate merge) — the user cart is already correct.
        }
    }

    private static Dictionary<string, AttributeValue> BuildItem(ShoppingCart cart)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["OwnerId"] = new(cart.OwnerId),
            ["Data"] = new(BasketSerializer.Serialize(cart))
        };

        // Only guest carts expire; user carts omit ExpiresAt so DynamoDB TTL never touches them.
        if (cart.IsGuest)
            item["ExpiresAt"] = new AttributeValue
            {
                N = DateTimeOffset.UtcNow.Add(GuestCartTtl).ToUnixTimeSeconds().ToString()
            };

        return item;
    }
}
