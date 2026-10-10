/**
 * Mirrors `PointsPolicy.ReviewCredit` in Challenges (SPEC-review-points). Display only: the
 * server decides and applies the credit; the SPA just tells the reviewer it is coming.
 */
export const REVIEW_CREDIT_POINTS = 15

/**
 * How long after publishing a review the balance is re-read. The credit is asynchronous
 * (Review stream → EventBridge → Challenges), so an immediate read would still show the old balance.
 */
export const REVIEW_CREDIT_REFRESH_DELAY_MS = 4000
