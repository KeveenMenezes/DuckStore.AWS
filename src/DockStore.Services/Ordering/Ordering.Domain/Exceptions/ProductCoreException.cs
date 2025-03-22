namespace Ordering.Domain.Exceptions;

public class ProductCoreException(ProductCoreError coreError)
    : CoreException(coreError)
{
}

public class ProductCoreError
    : CoreExceptionModel
{
    private ProductCoreError(string key, string message, Exception? innerException = null)
        : base(key, message, innerException)
    {
    }

    public static ProductCoreError ProductIdNotEmpty => new(
        "ProductIdNotEmpty.",
        "ProductId cannot be empty.");
}