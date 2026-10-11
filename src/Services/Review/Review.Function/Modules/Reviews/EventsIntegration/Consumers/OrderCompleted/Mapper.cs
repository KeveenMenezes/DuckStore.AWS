using BuildingBlocks.Core.Exceptions;
using BuildingBlocks.Core.Validation;

namespace Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

public static class OrderCompletedMapper
{
    // Rejects a malformed payload instead of writing a row keyed by an empty Guid: the exception
    // fails the invocation, so EventBridge retries and the event lands in the consumer's DLQ.
    public static MarkReviewsEligibleCommand ToCommand(OrderCompletedEvent message)
    {
        var failures = Validate(message).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new MarkReviewsEligibleCommand(message.CustomerId, message.ProductIds);
    }

    private static IEnumerable<ValidationFailure> Validate(OrderCompletedEvent message)
    {
        if (message.CustomerId == Guid.Empty)
        {
            yield return new(nameof(message.CustomerId), "CustomerId is required");
        }

        if (message.ProductIds is null || message.ProductIds.Count == 0)
        {
            yield return new(nameof(message.ProductIds), "ProductIds should not be empty");
        }
        else if (message.ProductIds.Contains(Guid.Empty))
        {
            yield return new(nameof(message.ProductIds), "ProductIds must not contain an empty id");
        }
    }
}
