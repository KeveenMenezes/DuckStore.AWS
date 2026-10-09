export type PointsReason = "challenge" | "review" | "redemption"

/** One movement of the balance. `points` is signed: positive for credits, negative for redemptions. */
export interface PointsEntry {
  id: string
  reason: PointsReason
  points: number
  createdAt: string
}

export interface PointsHistoryPage {
  entries: PointsEntry[]
  nextToken: string | null
}
