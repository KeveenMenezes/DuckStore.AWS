"use client"

import { useScoreContext } from "@/features/challenges/context/score-context"

/** Public hook for accessing challenge score state. */
export function useScore() {
  return useScoreContext()
}
