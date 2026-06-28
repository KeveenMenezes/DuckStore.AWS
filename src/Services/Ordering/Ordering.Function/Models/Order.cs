using BuildingBlocks.Core.DomainModel;

namespace Ordering.Function.Models;

public class Order : Aggregate<OrderId>
{
    public static Order Create(
        OrderId id,
        CustomerId customerId,
        OrderName orderName,
        Address shippingAddress,
        Payment payment)
    {
        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            OrderName = orderName,

            ShippingAddress = shippingAddress,
            Payment = payment,

            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        return order;
    }

    public void Add(ProductId productId, int quantity, decimal price)
    {
        var orderItem = new OrderItem(Id, productId, quantity, price);

        _orderItems.Add(orderItem);
    }

    public static Order Load(
        Guid id,
        Guid customerId,
        string orderName,
        Address shippingAddress,
        Payment payment,
        OrderStatus status,
        IEnumerable<OrderItem> orderItems,
        DateTime? createdAt = null,
        DateTime? lastModified = null)
    {
        var order = new Order
        {
            Id = OrderId.Of(id),
            CustomerId = CustomerId.Of(customerId),
            OrderName = OrderName.Of(orderName),
            ShippingAddress = shippingAddress,
            Payment = payment,
            Status = status,
            CreatedAt = createdAt,
            LastModified = lastModified
        };

        order._orderItems.AddRange(orderItems);

        return order;
    }

    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyList<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public CustomerId CustomerId { get; private set; } = null!;
    public OrderName OrderName { get; private set; } = null!;
    public Address ShippingAddress { get; private set; } = null!;
    public Payment Payment { get; private set; } = null!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal TotalPrice => OrderItems.Sum(x => x.Price * x.Quantity);
}
