namespace Basket.Function.Models;

public class ShoppingCartItem
{
    private ShoppingCartItem(Guid productId, string productName, string imageUrl, string color, int quantity, decimal price)
    {
        ProductId = productId;
        ProductName = productName;
        ImageUrl = imageUrl;
        Color = color;
        Quantity = quantity;
        Price = price;
    }

    public Guid ProductId { get; }
    public string ProductName { get; }
    public string ImageUrl { get; }
    public string Color { get; }
    public int Quantity { get; }
    public decimal Price { get; private set; }

    public static ShoppingCartItem Create(
        Guid productId, string productName, string imageUrl, string color, int quantity, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        return new ShoppingCartItem(productId, productName, imageUrl, color, quantity, price);
    }

    // Reconstitutes a persisted item (skips re-validation).
    public static ShoppingCartItem Load(
        Guid productId, string productName, string? imageUrl, string color, int quantity, decimal price) =>
        new(productId, productName, imageUrl ?? string.Empty, color, quantity, price);

    // A coupon reduces the unit price, but never below zero.
    public void ApplyDiscount(int amount) => Price = Math.Max(0, Price - amount);
}
