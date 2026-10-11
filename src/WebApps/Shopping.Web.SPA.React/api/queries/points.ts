// Cognito required — the ledger belongs to a signed-in player (ADR-0048 §1). Newest first; the
// balance is NOT derived from these rows, it comes from myChallengeProgress.score (ADR-0048 §2).
export const GET_MY_POINTS_HISTORY = `
  query GetMyPointsHistory($pageSize: Int, $nextToken: String) {
    myPointsHistory(pageSize: $pageSize, nextToken: $nextToken) {
      items {
        id
        type
        status
        points
        createdAt
        orderId
      }
      nextToken
    }
  }
`
