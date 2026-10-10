using Review.Function.Modules.Reviews.Data;

namespace Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

// The customer (Cognito sub) who completed an order and the distinct products it contained.
public sealed record MarkReviewsEligibleCommand(Guid CustomerId, IReadOnlyList<Guid> ProductIds);

public sealed class OrderCompletedHandler(IReviewEligibilityRepository repository)
{
    // Returns how many Eligible rows were created; products the customer already has a row for
    // are skipped by the repository's conditional write and do not stop the rest.
    public async Task<int> HandleAsync(MarkReviewsEligibleCommand command, CancellationToken cancellationToken = default)
    {
        var createdAt = DateTime.UtcNow;
        var created = 0;

        foreach (var productId in command.ProductIds)
        {
            if (await repository.TryCreateEligibleAsync(productId, command.CustomerId, createdAt, cancellationToken))
            {
                created++;
            }
        }

        return created;
    }
}
