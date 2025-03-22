namespace Ordering.Domain.Exceptions;

public class CoreExceptionModel(string key, string message, Exception? innerException = null)
{
    public string? Key { get; set; } = key;

    public string? Message { get; set; } = message;

    public Exception? InnerException { get; set; } = innerException;
}
