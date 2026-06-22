using BuildingBlocks.Core.DomainModel;

namespace Ordering.Domain.AggregatesModel.OrderAggregate.Models;

public class OrderItem : Entity<OrderItemId>
{
    public OrderItem(
        OrderId orderId,
        ProductId productId,
        int quantity,
        decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        Id = OrderItemId.Of(Guid.NewGuid());
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        Price = price;
    }

    public OrderId OrderId { get; private set; }
    public ProductId ProductId { get; private set; }
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }

    // Reconstitui um OrderItem já persistido (preserva o Id original, sem revalidar).
    private OrderItem(OrderItemId id, OrderId orderId, ProductId productId, int quantity, decimal price)
    {
        Id = id;
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        Price = price;
    }

    public static OrderItem Load(Guid id, Guid orderId, Guid productId, int quantity, decimal price) =>
        new(
            OrderItemId.Of(id),
            Domain.AggregatesModel.OrderAggregate.ValueObjects.OrderId.Of(orderId),
            ProductId.Of(productId),
            quantity,
            price);
}
