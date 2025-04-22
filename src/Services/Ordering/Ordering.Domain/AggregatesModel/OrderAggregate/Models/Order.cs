namespace Ordering.Domain.AggregatesModel.OrderAggregate.Models;

public class Order : Aggregate<OrderId>
{
    public static Order Create(
        OrderId id,
        CustomerId customerId,
        OrderName orderName,
        Address shippingAddress,
        Address billingAddress,
        Payment payment)
    {
        ValidateId(id?.Value);
        ValidateId(customerId?.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderName?.Value);

        if (shippingAddress is null)
        {
            throw new OrderCoreException(OrderCoreError.ShippingAddressNotNull);
        }

        if (billingAddress is null)
        {
            throw new OrderCoreException(OrderCoreError.BillingAddressNotNull);
        }

        if (payment is null)
        {
            throw new OrderCoreException(OrderCoreError.PaymentNotNull);
        }

        var order = new Order
        {
            Id = id!,
            CustomerId = customerId!,
            OrderName = orderName,

            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            Payment = payment,

            Status = OrderStatus.Pending
        };

        order.AddDomainEvent(new OrderCreatedEvent(order));

        return order;
    }

    public void Update(
        OrderName orderName,
        Address shippingAddress,
        Address billingAddress,
        Payment payment,
        OrderStatus status)
    {
        if (shippingAddress is null)
        {
            throw new OrderCoreException(OrderCoreError.ShippingAddressNotNull);
        }

        if (billingAddress is null)
        {
            throw new OrderCoreException(OrderCoreError.BillingAddressNotNull);
        }

        if (payment is null)
        {
            throw new OrderCoreException(OrderCoreError.PaymentNotNull);
        }

        OrderName = orderName;
        ShippingAddress = shippingAddress;
        BillingAddress = billingAddress;
        Payment = payment;
        Status = status;

        AddDomainEvent(new OrderUpdatedEvent(this));
    }

    public void Add(ProductId productId, int quantity, decimal price)
    {

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        if (productId is null)
        {
            throw new ArgumentNullException(nameof(productId), "ProductId cannot be null.");
        }

        var orderItem = new OrderItem(Id, productId, quantity, price);
        _orderItems.Add(orderItem);
    }

    public void Remove(ProductId productId)
    {
        if (productId is null)
        {
            throw new ArgumentNullException(nameof(productId), "ProductId cannot be null.");
        }

        var orderItem = _orderItems.FirstOrDefault(x => x.ProductId == productId) ??
            throw new InvalidOperationException("The product does not exist in the order.");

        _orderItems.Remove(orderItem);
    }

    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyList<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public CustomerId CustomerId { get; private set; } = default!;
    public OrderName OrderName { get; private set; } = default!;
    public Address ShippingAddress { get; private set; } = default!;
    public Address BillingAddress { get; private set; } = default!;
    public Payment Payment { get; private set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal TotalPrice
    {
        get => OrderItems.Sum(x => x.Price * x.Quantity);
        private set { /* Required for mapping */ }
    }

    private static void ValidateId(Guid? value)
    {
        if (value is null || value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Id cannot be null or empty.");
        }
    }
}