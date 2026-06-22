using BuildingBlocks.Core.DomainModel;

namespace Ordering.Domain.AggregatesModel.OrderAggregate.Models;

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

            Status = OrderStatus.Pending
        };

        order.AddDomainEvent(new OrderCreatedEvent(order));

        return order;
    }

    public void Update(
        OrderName orderName,
        Address shippingAddress,
        Payment payment,
        OrderStatus status)
    {
        OrderName = orderName;
        ShippingAddress = shippingAddress;
        Payment = payment;
        Status = status;

        AddDomainEvent(new OrderUpdatedEvent(this));
    }

    public void Add(ProductId productId, int quantity, decimal price)
    {
        var orderItem = new OrderItem(Id, productId, quantity, price);

        _orderItems.Add(orderItem);
    }

    public void Remove(ProductId productId)
    {
        var orderItem = _orderItems.FirstOrDefault(x => x.ProductId == productId) ??
            throw new ProductNotInOrderBadRequestException(productId.Value);

        _orderItems.Remove(orderItem);
    }

    // Reconstitui um Order já persistido (preserva Status/Id originais, sem disparar domain events).
    public static Order Load(
        Guid id,
        Guid customerId,
        string orderName,
        Address shippingAddress,
        Payment payment,
        OrderStatus status,
        IEnumerable<OrderItem> orderItems)
    {
        var order = new Order
        {
            Id = OrderId.Of(id),
            CustomerId = CustomerId.Of(customerId),
            OrderName = OrderName.Of(orderName),
            ShippingAddress = shippingAddress,
            Payment = payment,
            Status = status
        };

        order._orderItems.AddRange(orderItems);

        return order;
    }

    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyList<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public CustomerId CustomerId { get; private set; } = default!;
    public OrderName OrderName { get; private set; } = default!;
    public Address ShippingAddress { get; private set; } = default!;
    public Payment Payment { get; private set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal TotalPrice
    {
        get => OrderItems.Sum(x => x.Price * x.Quantity);
        private set { /* Required for mapping */ }
    }
}
