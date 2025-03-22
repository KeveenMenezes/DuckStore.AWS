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

    public static OrderCoreError OrderIdNotEmpty => new(
        "OrderIdNotEmpty.",
        "OrderId cannot be empty.");
}