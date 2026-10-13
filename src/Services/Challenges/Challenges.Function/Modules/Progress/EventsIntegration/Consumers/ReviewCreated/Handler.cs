namespace Challenges.Function.Modules.Progress.EventsIntegration.Consumers.ReviewCreated;

// The player who published a review and the product it is about.
public sealed record CreditReviewCommand(OwnerId OwnerId, Guid ProductId);

public sealed class ReviewCreatedHandler(IPlayerProgressRepository repository)
{
    // Returns false when this product was already credited to the player — a redelivered event,
    // or a review deleted and published again. The review's Status is never
    // the lock: it can go back and forth, or disappear by TTL.
    public Task<bool> HandleAsync(CreditReviewCommand command, CancellationToken cancellationToken = default)
    {
        var credit = PointsTransaction.ReviewCredit(command.OwnerId, command.ProductId, DateTime.UtcNow);

        return repository.CreditAsync(credit, cancellationToken);
    }
}
