"use client"

import { useEffect, useState } from "react"
import { gql } from "@/api"
import { GET_REWARD_CONVERSION } from "@/api/queries/pricing"
import type { GqlRewardConversion } from "@/graphql/types"

/**
 * Pricing's points → currency rate (ADR-0048 §1, §3): the SPA never hard-codes it, so the preview
 * next to the points field follows reward-config.ts. Cognito-only — pass `enabled = false` for a
 * visitor. Null until loaded or when the request failed (the caller then shows no preview).
 */
export function useRewardConversion(enabled: boolean) {
  const [conversion, setConversion] = useState<GqlRewardConversion | null>(null)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    gql<{ rewardConversion: GqlRewardConversion }>(GET_REWARD_CONVERSION)
      .then((data) => {
        if (!cancelled) setConversion(data.rewardConversion)
      })
      .catch((error) => console.error("Failed to fetch the points conversion rate", error))
    return () => { cancelled = true }
  }, [enabled])

  return conversion
}

/** The currency value of `points` at Pricing's rate, rounded down to cents like Pricing does. */
export function pointsToCurrency(points: number, conversion: GqlRewardConversion): number {
  return Math.floor((points * conversion.currencyPerUnit * 100) / conversion.pointsPerUnit) / 100
}
