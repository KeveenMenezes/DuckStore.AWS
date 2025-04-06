namespace Ordering.Domain.Exceptions;

public class CoreException
    : Exception
{
    protected CoreException(CoreExceptionModel coreError)
        : base(
            coreError?.Message,
            coreError?.InnerException)
    {
        ArgumentNullException.ThrowIfNull(coreError);

        CoreError = coreError;
    }

    public CoreExceptionModel CoreError { get; }
}
