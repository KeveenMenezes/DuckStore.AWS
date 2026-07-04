namespace Basket.Function.Data;

// Single source of truth for the cart's stored JSON shape (the DynamoDB `Data` attribute).
// Keeping it here lets the ShoppingCart aggregate stay encapsulated while the persisted
// item avoids unused aggregate audit fields.
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
            cart.OwnerId,
            cart.Items
                .Select(item => new ItemSnapshot(
                    item.Quantity, item.Color, item.Price, item.ProductId, item.ProductName, item.ImageUrl))
                .ToList(),
            cart.TotalPrice);

    private static ShoppingCart ToCart(CartSnapshot snapshot) =>
        ShoppingCart.Load(
            snapshot.OwnerId,
            snapshot.Items.Select(item =>
                ShoppingCartItem.Load(item.ProductId, item.ProductName, item.ImageUrl, item.Color, item.Quantity, item.Price)));

    // Field names/casing are the cart's stored contract — also read by the SPA `basket`
    // GraphQL resolver (PascalCase from the .NET serializer). TotalPrice is written for that
    // consumer and recomputed from the items on read.
    private sealed record CartSnapshot(string OwnerId, List<ItemSnapshot> Items, decimal TotalPrice);

    // ImageUrl is nullable to handle items persisted before this field was added.
    private sealed record ItemSnapshot(int Quantity, string Color, decimal Price, Guid ProductId, string ProductName, string? ImageUrl);
}
