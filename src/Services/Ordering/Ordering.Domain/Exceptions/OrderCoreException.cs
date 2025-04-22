namespace Ordering.Domain.Exceptions;

public class OrderCoreException(OrderCoreError coreError)
    : CoreException(coreError)
{
}

public class OrderCoreError
    : CoreExceptionModel
{
    private OrderCoreError(string key, string message, Exception? innerException = null)
        : base(key, message, innerException)
    {
    }

    public static OrderCoreError OrderIdNotEmpty =>
        new(
            "OrderIdNotEmpty.",
            "OrderId cannot be empty.");

    public static OrderCoreError OrderNotFound(Guid orderId) =>
        new(
            "OrderNotFound",
            $"Order with id {orderId} was not found.");

    public static OrderCoreError DuplicateProductInOrder(Guid productId) =>
        new(
            "DuplicateProductInOrder",
            $"The product with id {productId} is already added to the order.");

    public static OrderCoreError ProductNotInOrder(Guid productId) =>
        new(
            "ProductNotInOrder",
            $"The product with id {productId} does not exist in the order.");

    public static OrderCoreError InvalidOrderStatus(string status) =>
        new(
            "InvalidOrderStatus",
            $"The order status '{status}' is invalid.");

    public static OrderCoreError PaymentNotNull =>
        new(
            "PaymentNotNull",
            "Payment cannot be null.");

    public static OrderCoreError ShippingAddressNotNull =>
        new(
            "ShippingAddressNotNull",
            "Shipping address cannot be null.");

    public static OrderCoreError BillingAddressNotNull =>
        new(
            "BillingAddressNotNull",
            "Billing address cannot be null.");
}
