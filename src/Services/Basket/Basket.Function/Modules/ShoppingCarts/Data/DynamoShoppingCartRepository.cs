namespace Basket.Function.Modules.ShoppingCarts.Data;

public class DynamoShoppingCartRepository(IAmazonDynamoDB dynamoDb)
    : IShoppingCartRepository
{
    public const string TableName = "shopping-carts";

    // Sliding TTL window for guest carts; renewed on every write (see BuildItem).
    private static readonly TimeSpan GuestCartTtl = TimeSpan.FromDays(15);

    // How long a CHECKOUT# record answers replays of its attempt (ADR-0050 §4).
    private static readonly TimeSpan CheckoutRecordTtl = TimeSpan.FromHours(24);

    public async Task<ShoppingCart> GetBasket(string ownerId, CancellationToken cancellationToken) =>
        await TryGetBasket(ownerId, cancellationToken) ?? throw new ShoppingCartNotFoundException(ownerId);

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
            ShoppingCartSerializer.Deserialize(response.Item["Data"].S) :
            null;
    }

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        await dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = BuildItem(cart) },
            cancellationToken);

        return cart;
    }

    public async Task<CartCloseResult> CloseCartAsync(
        string ownerId, string checkoutId, string? cartVersion, Guid orderId, string checkoutDataJson,
        CancellationToken cancellationToken)
    {
        var checkoutKey = CheckoutKey(ownerId, checkoutId);

        // Both Delete conditions are false once the cart is gone, so two attempts with different
        // checkoutIds can never both close the same cart. The bare "Version = :v OR
        // attribute_not_exists(Version)" would pass on a missing item.
        var delete = cartVersion is null
            ? new Delete
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(ownerId) },
                ConditionExpression = "attribute_exists(OwnerId) AND attribute_not_exists(#version)",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#version"] = "Version" }
            }
            : new Delete
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(ownerId) },
                ConditionExpression = "#version = :version",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#version"] = "Version" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":version"] = new(cartVersion) }
            };

        var put = new Put
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["OwnerId"] = new(checkoutKey),
                ["Type"] = new("Checkout"),
                ["CheckoutData"] = new(checkoutDataJson),
                ["OrderId"] = new(orderId.ToString()),
                ["ExpiresAt"] = new AttributeValue
                {
                    N = DateTimeOffset.UtcNow.Add(CheckoutRecordTtl).ToUnixTimeSeconds().ToString()
                }
            },
            ConditionExpression = "attribute_not_exists(OwnerId)"
        };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems = [new TransactWriteItem { Put = put }, new TransactWriteItem { Delete = delete }]
                },
                cancellationToken);

            return new CartCloseResult(CartCloseOutcome.Closed, orderId);
        }
        catch (TransactionCanceledException ex) when (ConditionFailed(ex, 0))
        {
            // Checked first: on a replay the Delete fails too, because the first close removed the cart.
            return new CartCloseResult(CartCloseOutcome.Replayed, await StoredOrderIdAsync(checkoutKey, cancellationToken));
        }
        catch (TransactionCanceledException ex) when (ConditionFailed(ex, 1))
        {
            return new CartCloseResult(CartCloseOutcome.CartChanged, Guid.Empty);
        }
    }

    private async Task<Guid> StoredOrderIdAsync(string checkoutKey, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["OwnerId"] = new(checkoutKey) },
                ConsistentRead = true
            },
            cancellationToken);

        return Guid.Parse(response.Item["OrderId"].S);
    }

    private static string CheckoutKey(string ownerId, string checkoutId) => $"CHECKOUT#{ownerId}#{checkoutId}";

    private static bool ConditionFailed(TransactionCanceledException ex, int index) =>
        ex.CancellationReasons is { } reasons
        && reasons.Count > index
        && reasons[index].Code == "ConditionalCheckFailed";

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
            ["Data"] = new(ShoppingCartSerializer.Serialize(cart)),
            // A fresh token on every write: the checkout closes the cart only while its Version is
            // still the one Pricing quoted (ADR-0050 §4). The storeBasket resolver does the same.
            ["Version"] = new(Guid.NewGuid().ToString("N"))
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
