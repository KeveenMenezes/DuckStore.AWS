namespace Basket.Function.Modules.ShoppingCarts.Domain.Entities;

public class ShoppingCartItem
{
    private ShoppingCartItem(ProductId productId, string productName, string? imageId, string color, int quantity, decimal price)
    {
        ProductId = productId;
        ProductName = productName;
        ImageId = imageId;
        Color = color;
        Quantity = quantity;
        Price = price;
    }

    public ProductId ProductId { get; }
    public string ProductName { get; }

    // Snapshot of the product's main imageId at add-to-cart time (ADR-0034) — a key, never
    // a URL. Null for items persisted before the image pipeline (clients render a placeholder).
    public string? ImageId { get; }
    public string Color { get; }
    public int Quantity { get; }
    public decimal Price { get; }

    public static ShoppingCartItem Create(
        ProductId productId, string productName, string? imageId, string color, int quantity, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        return new ShoppingCartItem(productId, productName, imageId, color, quantity, price);
    }

    // Reconstitutes a persisted item (skips re-validation).
    public static ShoppingCartItem Load(
        Guid productId, string productName, string? imageId, string color, int quantity, decimal price) =>
        new(ProductId.Of(productId), productName, imageId, color, quantity, price);
}
