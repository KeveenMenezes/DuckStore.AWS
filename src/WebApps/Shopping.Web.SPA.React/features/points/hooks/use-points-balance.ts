"use client"

import { useAuth } from "@/features/auth/hooks/use-auth"
import { useScore } from "@/features/challenges/hooks/use-score"

/**
 * The signed-in player's points balance (ADR-0048 §2).
 *
 * Deliberately a view over `ScoreProvider`, not a second fetch: the balance IS
 * `myChallengeProgress.score`, and `ScoreProvider` already hydrates it and applies `newScore` after
 * `submitChallengeAnswer`. Reading from the same state is what keeps the header and the
 * challenges page the same number by construction. The ledger rows explain the balance; they are
 * never summed client-side.
 */
export function usePointsBalance() {
  const { user } = useAuth()
  const { score, isLoading } = useScore()

  return { balance: score, isAuthenticated: user !== null, isLoading }
}
