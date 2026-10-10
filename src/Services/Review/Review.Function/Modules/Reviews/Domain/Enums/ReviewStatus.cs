namespace Review.Function.Modules.Reviews.Domain.Enums;

// Lifecycle of a reviews row (ADR-0049 §1). Only Published rows carry GSI1PK/GSI1SK, so the
// sparse GSI1 behind reviewsByProduct structurally hides Eligible and Deleted rows.
public enum ReviewStatus
{
    // Created by review-order-completed-consumer: the customer bought the product and may review it.
    Eligible,

    // Written by createReview: public, listed and counted in the product's rating aggregate.
    Published,

    // Written by deleteReview: withdrawn from the aggregate; the TTL on ExpiresAt removes the row.
    Deleted
}
