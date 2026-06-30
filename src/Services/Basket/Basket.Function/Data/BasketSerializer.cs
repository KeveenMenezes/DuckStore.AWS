namespace Basket.Function.Data;

// Single source of truth for the cart's stored/cached JSON shape (the DynamoDB `Data`
// attribute and the Redis value). Keeping it here lets the ShoppingCart aggregate stay
// encapsulated while the persisted item avoids unused aggregate audit fields.
internal static class BasketSerializer
{
    public static string Serialize(ShoppingCart cart) =>
        JsonSerializer.Serialize(ToSnapshot(cart));

    public static ShoppingCart Deserialize(string json) =>
        ToCart(
            JsonSerializer.Deserialize<CartSnapshot>(json)
            ?? throw new InvalidOperationException("Invalid basket payload."));

    private static CartSnapshot ToSnapshot(ShoppingCart cart) =>
        new(
            cart.UserName,
            cart.Items
                .Select(item => new ItemSnapshot(
                    item.Quantity, item.Color, item.Price, item.ProductId, item.ProductName))
                .ToList(),
            cart.TotalPrice);

    private static ShoppingCart ToCart(CartSnapshot snapshot) =>
        ShoppingCart.Load(
            snapshot.UserName,
            snapshot.Items.Select(item =>
                ShoppingCartItem.Load(item.ProductId, item.ProductName, item.Color, item.Quantity, item.Price)));

    // Field names/casing are the cart's stored contract — also read by the SPA `basket`
    // GraphQL resolver (PascalCase from the .NET serializer). TotalPrice is written for that
    // consumer and recomputed from the items on read.
    private sealed record CartSnapshot(string UserName, List<ItemSnapshot> Items, decimal TotalPrice);

    private sealed record ItemSnapshot(int Quantity, string Color, decimal Price, Guid ProductId, string ProductName);
}
