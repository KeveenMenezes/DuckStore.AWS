import { gql } from "@/api"
import { GET_MY_POINTS_HISTORY } from "@/api/queries/points"
import type {
  GqlPointsHistoryPage,
  GqlPointsTransactionStatus,
  GqlPointsTransactionType,
} from "@/graphql/types"
import type { PointsHistoryPage, PointsReason, PointsStatus } from "@/features/points/types/points.types"

const REASON_FROM_GQL: Record<GqlPointsTransactionType, PointsReason> = {
  ChallengeCredit: "challenge",
  ReviewCredit: "review",
  Redemption: "redemption",
}

const STATUS_FROM_GQL: Record<GqlPointsTransactionStatus, PointsStatus> = {
  Completed: "completed",
  Reserved: "awaiting",
  Used: "used",
  Released: "released",
  Refunded: "refunded",
  Failed: "failed",
}

/**
 * Fetch one page of the signed-in player's points history, newest first (ADR-0048 §1).
 * Cognito-only — callers must not invoke this for a signed-out visitor.
 */
export async function getMyPointsHistory(
  pageSize = 20,
  nextToken?: string | null,
): Promise<PointsHistoryPage> {
  const data = await gql<{ myPointsHistory: GqlPointsHistoryPage }>(GET_MY_POINTS_HISTORY, {
    pageSize,
    nextToken: nextToken ?? null,
  })

  return {
    entries: data.myPointsHistory.items.map((item) => ({
      id: item.id,
      reason: REASON_FROM_GQL[item.type],
      status: STATUS_FROM_GQL[item.status],
      points: item.points,
      createdAt: item.createdAt,
    })),
    nextToken: data.myPointsHistory.nextToken,
  }
}
