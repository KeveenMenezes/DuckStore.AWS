namespace Review.Function.Modules.Reviews.Data;

// Write side of review eligibility (ADR-0049 §3): a purchase grants the customer one Eligible row
// per product, which createReview later promotes to Published.
public interface IReviewEligibilityRepository
{
    // Creates the Eligible row for (productId, customerId) only if no row exists yet. Returns false
    // when a row already exists — Eligible, Published or Deleted — which is left untouched
    // (SPEC-review-eligibility, criterion 8).
    Task<bool> TryCreateEligibleAsync(
        Guid productId, Guid customerId, DateTime createdAtUtc, CancellationToken cancellationToken = default);
}
