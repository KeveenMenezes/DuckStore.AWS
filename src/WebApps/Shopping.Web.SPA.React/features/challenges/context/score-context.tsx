"use client"

import {
  createContext,
  useContext,
  useState,
  useCallback,
  useMemo,
  useEffect,
  useRef,
  type ReactNode,
} from "react"
import { useAuth } from "@/features/auth/hooks/use-auth"
import {
  getMyChallengeProgress,
  submitChallengeAnswer,
  revealChallengeHint,
} from "@/features/challenges/services/challenges.service"
import { HINT_PENALTY } from "@/features/challenges/constants"
import type {
  ChallengeAttempt,
  ChallengeProgress,
  RevealHintResult,
  SubmitAnswerResult,
} from "@/features/challenges/types/challenge.types"

interface ScoreContextType {
  score: number
  completedChallenges: string[]
  /**
   * Every question already answered, keyed by id — wrong answers included. Answering is one-shot
   * (ADR-0045 §4), so this, not `completedChallenges`, is what decides whether a challenge may
   * still be attempted.
   */
  answeredChallenges: Record<string, ChallengeAttempt>
  hintsRevealed: Record<string, number>
  revealedHints: Record<string, string[]>
  isLoading: boolean
  getHintPenalty: (challengeId: string) => number
  submitAnswer: (challengeId: string, selectedOption: number) => Promise<SubmitAnswerResult>
  requestHint: (challengeId: string) => Promise<RevealHintResult>
  /** Seeds the context from server-fetched progress (e.g. the /challenges SSR page), skipping this provider's own client-side fetch. */
  hydrate: (progress: ChallengeProgress) => void
}

const ScoreContext = createContext<ScoreContextType | null>(null)

export function ScoreProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const [score, setScore] = useState(0)
  const [completedChallenges, setCompletedChallenges] = useState<string[]>([])
  const [answeredChallenges, setAnsweredChallenges] = useState<Record<string, ChallengeAttempt>>({})
  const [hintsRevealed, setHintsRevealed] = useState<Record<string, number>>({})
  const [revealedHints, setRevealedHints] = useState<Record<string, string[]>>({})
  const [isLoading, setIsLoading] = useState(true)
  // Which identity the state on screen belongs to — `null` for a signed-out visitor, `undefined`
  // before anything has been loaded. A ref (not state) so /challenges's SSR-seeded hydrate() call
  // — which runs in a descendant's effect, firing before this provider's own effect in the same
  // commit — reliably suppresses the redundant client-side fetch below, regardless of
  // render/commit timing. Keyed by user rather than a bare boolean because this provider lives in
  // the root tree and outlives any single session: a plain "already hydrated" latch would leave
  // one account's score in the header after a sign-out or a switch on the same tab.
  const userId = user?.id ?? null
  const hydratedForRef = useRef<string | null | undefined>(undefined)

  const applyProgress = useCallback((progress: ChallengeProgress) => {
    setScore(progress.score)
    setCompletedChallenges(progress.attempts.filter((a) => a.isCorrect).map((a) => a.questionId))
    setAnsweredChallenges(Object.fromEntries(progress.attempts.map((a) => [a.questionId, a])))
    setHintsRevealed(
      Object.fromEntries(progress.attempts.map((a) => [a.questionId, a.hintsRevealed])),
    )
  }, [])

  const resetProgress = useCallback(() => {
    setScore(0)
    setCompletedChallenges([])
    setAnsweredChallenges({})
    setHintsRevealed({})
    setRevealedHints({})
  }, [])

  const hydrate = useCallback(
    (progress: ChallengeProgress) => {
      hydratedForRef.current = userId
      applyProgress(progress)
      setIsLoading(false)
    },
    [applyProgress, userId],
  )

  // Client-side fallback hydration for any page other than /challenges (e.g. the header's score
  // badge, or /my-profile) — /challenges itself fetches server-side and calls hydrate() instead
  // (ADR-0045 §10: progress is personalized, so that fetch is SSR, not static).
  useEffect(() => {
    if (hydratedForRef.current === userId) return

    hydratedForRef.current = userId

    let cancelled = false
    // Routed through a promise even for the signed-out case so every setState below runs inside a
    // callback, never synchronously in the effect body itself.
    const fetchProgress = userId ? getMyChallengeProgress() : Promise.resolve(null)

    fetchProgress
      .then((progress) => {
        if (cancelled) return
        // Reset unconditionally before applying: whatever is on screen belongs to somebody else
        // (or to nobody), and a sign-out has no progress to overwrite it with. Without this the
        // previous account's score stays in the header until a reload.
        resetProgress()
        if (progress) applyProgress(progress)
      })
      .catch((error) => {
        if (cancelled) return
        console.error("Failed to hydrate challenge progress", error)
        resetProgress()
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [userId, applyProgress, resetProgress])

  // Display-only estimate shown before a submission — the real penalty is whatever the server
  // actually applied, returned in SubmitAnswerResult.pointsEarned (ADR-0045 §10).
  const getHintPenalty = useCallback(
    (challengeId: string) => (hintsRevealed[challengeId] ?? 0) * HINT_PENALTY,
    [hintsRevealed],
  )

  const submitAnswer = useCallback(
    async (challengeId: string, selectedOption: number) => {
      const result = await submitChallengeAnswer(challengeId, selectedOption)
      // Recorded whatever the verdict: a wrong answer closes the question just as firmly as a right
      // one (ADR-0045 §4), and leaving it out is what let a second submission be offered at all.
      // result.selectedOption, not the argument — on a re-submission the server answers with the
      // attempt already on record, and that is the option this verdict describes.
      setAnsweredChallenges((prev) => ({
        ...prev,
        [challengeId]: {
          questionId: challengeId,
          isCorrect: result.isCorrect,
          selectedOption: result.selectedOption,
          hintsRevealed: hintsRevealed[challengeId] ?? 0,
          pointsEarned: result.pointsEarned,
          answeredAt: new Date().toISOString(),
        },
      }))
      if (result.isCorrect) {
        setScore(result.newScore)
        setCompletedChallenges((prev) => (prev.includes(challengeId) ? prev : [...prev, challengeId]))
      }
      return result
    },
    [hintsRevealed],
  )

  const requestHint = useCallback(async (challengeId: string) => {
    const result = await revealChallengeHint(challengeId)
    setHintsRevealed((prev) => ({ ...prev, [challengeId]: result.hintsRevealed }))
    setRevealedHints((prev) => ({
      ...prev,
      [challengeId]: [...(prev[challengeId] ?? []), result.hint],
    }))
    return result
  }, [])

  const value = useMemo(
    () => ({
      score,
      completedChallenges,
      answeredChallenges,
      hintsRevealed,
      revealedHints,
      isLoading,
      getHintPenalty,
      submitAnswer,
      requestHint,
      hydrate,
    }),
    [
      score,
      completedChallenges,
      answeredChallenges,
      hintsRevealed,
      revealedHints,
      isLoading,
      getHintPenalty,
      submitAnswer,
      requestHint,
      hydrate,
    ],
  )

  return <ScoreContext.Provider value={value}>{children}</ScoreContext.Provider>
}

export function useScoreContext() {
  const context = useContext(ScoreContext)
  if (!context) throw new Error("useScore must be used within ScoreProvider")
  return context
}
