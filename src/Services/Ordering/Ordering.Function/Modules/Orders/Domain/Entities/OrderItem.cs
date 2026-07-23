namespace Ordering.Function.Modules.Orders.Domain.Entities;

public class OrderItem : Entity<OrderItemId>
{
    public OrderItem(
        OrderId orderId,
        ProductId productId,
        string productName,
        string? imageId,
        int quantity,
        decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        Id = OrderItemId.Of(Guid.NewGuid());
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        ImageId = imageId;
        Quantity = quantity;
        Price = price;
    }

    public OrderId OrderId { get; private set; }
    public ProductId ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string? ImageId { get; private set; }
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }

    // Reconstitutes a persisted OrderItem (preserves the original Id, skips re-validation).
    private OrderItem(
        OrderItemId id, OrderId orderId, ProductId productId, string productName, string? imageId,
        int quantity, decimal price)
    {
        Id = id;
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        ImageId = imageId;
        Quantity = quantity;
        Price = price;
    }

    public static OrderItem Load(
        Guid id, Guid orderId, Guid productId, string productName, string? imageId, int quantity, decimal price) =>
        new(
            OrderItemId.Of(id),
            Ordering.Function.Modules.Orders.Domain.ValueObjects.OrderId.Of(orderId),
            ProductId.Of(productId),
            productName,
            imageId,
            quantity,
            price);
}
