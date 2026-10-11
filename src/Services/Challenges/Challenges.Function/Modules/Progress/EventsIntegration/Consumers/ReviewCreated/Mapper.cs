namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.ReviewCreated;

public static class ReviewCreatedMapper
{
    // Rejects a malformed payload instead of crediting nobody: the exception fails the invocation,
    // so EventBridge retries and the event lands in the consumer's DLQ.
    public static CreditReviewCommand ToCommand(ReviewCreatedEvent message)
    {
        var failures = Validate(message).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // UserId is the reviewer's Cognito sub (ADR-0049 §5); Challenges keys players by
        // USER#<sub> (ADR-0045 §7).
        return new CreditReviewCommand(OwnerId.Of($"USER#{message.UserId}"), message.ProductId);
    }

    private static IEnumerable<ValidationFailure> Validate(ReviewCreatedEvent message)
    {
        if (string.IsNullOrWhiteSpace(message.UserId))
        {
            yield return new(nameof(message.UserId), "UserId is required");
        }

        if (message.ProductId == Guid.Empty)
        {
            yield return new(nameof(message.ProductId), "ProductId is required");
        }
    }
}
