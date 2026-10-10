"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { getMyPointsHistory } from "@/features/points/services/points.service"
import type { PointsEntry } from "@/features/points/types/points.types"

/**
 * Paginated points history for the signed-in player. `enabled` gates the first fetch so a visitor
 * (or a session still being restored) never calls the Cognito-only query.
 */
export function usePointsHistory(enabled: boolean) {
  const [entries, setEntries] = useState<PointsEntry[]>([])
  const [nextToken, setNextToken] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  // False until the first attempt settles, so the view can tell "not asked yet" from "empty".
  const [hasLoaded, setHasLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const started = useRef(false)

  const loadPage = useCallback(async (token: string | null) => {
    if (inFlight.current) return
    inFlight.current = true
    setIsLoading(true)
    setError(null)
    try {
      const page = await getMyPointsHistory(20, token)
      setEntries((prev) => (token ? [...prev, ...page.entries] : page.entries))
      setNextToken(page.nextToken)
    } catch (e) {
      console.error("Failed to load points history", e)
      setError("Could not load your points history. Please try again.")
    } finally {
      inFlight.current = false
      setIsLoading(false)
      setHasLoaded(true)
    }
  }, [])

  useEffect(() => {
    if (!enabled || started.current) return
    started.current = true
    void loadPage(null)
  }, [enabled, loadPage])

  const loadMore = useCallback(() => {
    if (nextToken) void loadPage(nextToken)
  }, [nextToken, loadPage])

  const retry = useCallback(() => void loadPage(nextToken), [nextToken, loadPage])

  return { entries, isLoading, hasLoaded, error, hasMore: nextToken !== null, loadMore, retry }
}
