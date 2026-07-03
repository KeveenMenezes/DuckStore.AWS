"use client"

import { createContext, useContext, useState, useCallback, useMemo, type ReactNode } from "react"
import { HINT_PENALTY } from "@/features/challenges/constants"

interface ScoreContextType {
  score: number
  completedChallenges: string[]
  addScore: (challengeId: string, points: number) => boolean
  hintsUsed: Record<string, number>
  spendHint: (challengeId: string) => void
  getHintPenalty: (challengeId: string) => number
}

const ScoreContext = createContext<ScoreContextType | null>(null)

export function ScoreProvider({ children }: { children: ReactNode }) {
  const [score, setScore] = useState(0)
  const [completedChallenges, setCompletedChallenges] = useState<string[]>([])
  const [hintsUsed, setHintsUsed] = useState<Record<string, number>>({})

  const getHintPenalty = useCallback(
    (challengeId: string) => (hintsUsed[challengeId] || 0) * HINT_PENALTY,
    [hintsUsed],
  )

  const addScore = useCallback(
    (challengeId: string, points: number): boolean => {
      if (completedChallenges.includes(challengeId)) return false
      const penalty = getHintPenalty(challengeId)
      const finalPoints = Math.max(0, points - penalty)
      setScore((prev) => prev + finalPoints)
      setCompletedChallenges((prev) => [...prev, challengeId])
      return true
    },
    [completedChallenges, getHintPenalty],
  )

  const spendHint = useCallback((challengeId: string) => {
    setHintsUsed((prev) => ({
      ...prev,
      [challengeId]: (prev[challengeId] || 0) + 1,
    }))
  }, [])

  const value = useMemo(
    () => ({ score, completedChallenges, addScore, hintsUsed, spendHint, getHintPenalty }),
    [score, completedChallenges, addScore, hintsUsed, spendHint, getHintPenalty],
  )

  return <ScoreContext.Provider value={value}>{children}</ScoreContext.Provider>
}

export function useScoreContext() {
  const context = useContext(ScoreContext)
  if (!context) throw new Error("useScore must be used within ScoreProvider")
  return context
}
