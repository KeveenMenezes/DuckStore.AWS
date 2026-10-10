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

/**
 * 100 points = R$1 (infra/constructs/reward-config.ts). Only for the "N points = R$ X" preview next
 * to the input: the discount actually applied is the quote's `pointsDiscount`, computed by Pricing.
 */
export const POINTS_PER_REAL = 100

/**
 * How long after a checkout with points the balance is re-read: Challenges reserves (debits) them
 * off the checkout event, asynchronously.
 */
export const REDEMPTION_REFRESH_DELAY_MS = 4000
