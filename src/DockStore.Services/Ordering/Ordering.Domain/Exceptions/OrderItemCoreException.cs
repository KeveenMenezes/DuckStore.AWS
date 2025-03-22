namespace Ordering.Domain.Exceptions;

public class OrderItemCoreException(OrderItemCoreError coreError)
    : CoreException(coreError)
{
}

public class OrderItemCoreError
    : CoreExceptionModel
{
    private OrderItemCoreError(string key, string message, Exception? innerException = null)
        : base(key, message, innerException)
    {
    }

    public static OrderItemCoreError OrderItemIdNotEmpty => new(
        "OrderItemIdNotEmpty.",
        "OrderItemId cannot be empty.");
}
