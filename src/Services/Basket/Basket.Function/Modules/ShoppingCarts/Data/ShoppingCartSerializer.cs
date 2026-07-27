using System.Text.Json.Serialization;
using Basket.Function.Shared.Configuration;
namespace Basket.Function.Modules.ShoppingCarts.Data;

// Single source of truth for the cart's stored JSON shape (the DynamoDB `Data` attribute).
// Keeping it here lets the ShoppingCart aggregate stay encapsulated while the persisted
// item avoids unused aggregate audit fields.
internal static class ShoppingCartSerializer
{
    public static string Serialize(ShoppingCart cart) =>
        JsonSerializer.Serialize(ToSnapshot(cart), CartStorageSerializerContext.Default.CartSnapshot);

    public static ShoppingCart Deserialize(string json) =>
        ToCart(
            JsonSerializer.Deserialize(json, CartStorageSerializerContext.Default.CartSnapshot)
            ?? throw new InvalidOperationException("Invalid basket payload."));

    private static CartSnapshot ToSnapshot(ShoppingCart cart) =>
        new(
            cart.OwnerId,
            cart.Items
                .Select(item => new ItemSnapshot(
                    item.Quantity, item.Color, item.Price, item.ProductId.Value, item.ProductName, item.ImageId))
                .ToList(),
            cart.TotalPrice);

    private static ShoppingCart ToCart(CartSnapshot snapshot) =>
        ShoppingCart.Load(
            snapshot.OwnerId,
            snapshot.Items.Select(item =>
                ShoppingCartItem.Load(item.ProductId, item.ProductName, item.ImageId, item.Color, item.Quantity, item.Price)));

    // Field names/casing are the cart's stored contract — also read by the SPA `basket`
    // GraphQL resolver (PascalCase from the .NET serializer). TotalPrice is written for that
    // consumer and recomputed from the items on read.
    internal sealed record CartSnapshot(string OwnerId, List<ItemSnapshot> Items, decimal TotalPrice);

    // ImageId is nullable to handle items persisted before the field existed — including
    // pre-ADR-0034 carts whose snapshot carried ImageUrl instead (that value is dropped;
    // clients render a placeholder for those items).
    internal sealed record ItemSnapshot(int Quantity, string Color, decimal Price, Guid ProductId, string ProductName, string? ImageId);
}

/// <summary>
/// JSON contract for the cart's stored shape only.
/// </summary>
/// <remarks>
/// Separate from BasketSerializerContext on purpose: the snapshot is a persistence detail of this
/// class, not part of any Lambda's request/response surface, and Native AOT needs a compile-time
/// contract for it all the same (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(ShoppingCartSerializer.CartSnapshot))]
internal partial class CartStorageSerializerContext : JsonSerializerContext
{
}
