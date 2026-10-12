export type PointsReason = "challenge" | "review" | "redemption"

/**
 * Where a row stands (ADR-0048 §5). Credits are always "completed"; a redemption is "awaiting"
 * payment (reserved), then "used", or gives the points back ("released" before payment, "refunded"
 * after), or "failed" when the balance was short — which never debited anything.
 */
export type PointsStatus = "completed" | "awaiting" | "used" | "released" | "refunded" | "failed"

/** One movement of the balance. `points` is signed: positive for credits, negative for redemptions. */
export interface PointsEntry {
  id: string
  reason: PointsReason
  status: PointsStatus
  points: number
  createdAt: string
}

export interface PointsHistoryPage {
  entries: PointsEntry[]
  nextToken: string | null
}
