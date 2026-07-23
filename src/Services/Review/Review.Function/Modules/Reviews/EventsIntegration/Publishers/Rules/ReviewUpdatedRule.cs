namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

// Fires on an edit to an existing review (MODIFY — the composite Id from ADR-0029 means a
// resubmission by the same customer for the same product overwrites the row instead of inserting
// a new one) and publishes ReviewUpdated so CatalogView applies a rating delta: ratingCount stays
// unchanged, ratingSum += (NewRating - OldRating) (ADR-0029, fulfilling the negative-delta support
// ADR-0011's "Future Constraints" already anticipated).
//
// Matches unconditionally on MODIFY, even when the rating itself didn't change (a comment-only
// edit) — this event is also what drives ISR revalidation of the product page's review list, so a
// comment-only edit still needs to invalidate that cache. A zero rating delta is a harmless no-op
// on the CatalogView side.
public sealed class ReviewUpdatedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) => context.EventName == "MODIFY";

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ReviewStreamImage> context, CancellationToken cancellationToken = default)
    {
        var oldReview = context.Old!;
        var newReview = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(ReviewUpdatedEvent),
            new ReviewUpdatedEvent
            {
                ReviewId = newReview.Id,
                ProductId = newReview.ProductId,
                OldRating = oldReview.Rating,
                NewRating = newReview.Rating
            }));
    }
}
