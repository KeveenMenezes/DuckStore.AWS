namespace Ordering.Domain.Exceptions;

public class CustomerCoreException(CustomerCoreError coreError)
    : CoreException(coreError)
{
}

public class CustomerCoreError
    : CoreExceptionModel
{
    private CustomerCoreError(string key, string message, Exception? innerException = null)
        : base(key, message, innerException)
    {
    }

    public static CustomerCoreError CustomerIdNotEmpty => new(
        "CustomerIdNotEmpty.",
        "CustomerId cannot be empty.");
}
