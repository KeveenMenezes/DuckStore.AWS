"use client"

import { useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Code2 } from "lucide-react"
import { useChallenges } from "@/features/challenges/hooks/use-challenges"
import { useScore } from "@/features/challenges/hooks/use-score"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { ChallengeStats } from "@/features/challenges/components/challenge-stats"
import { ChallengeFilters } from "@/features/challenges/components/challenge-filters"
import { ChallengeCard } from "@/features/challenges/components/challenge-card"
import type { Challenge, ChallengeProgress } from "@/features/challenges/types/challenge.types"

interface ChallengesViewProps {
  /** Server-fetched challenge list, seeded into the filter hook. */
  initialChallenges: Challenge[]
  /** Server-fetched progress (ADR-0045 §10 — personalized, so this page is SSR); null for a
   * signed-out visitor. */
  initialProgress: ChallengeProgress | null
}

/**
 * Client experience layer for the challenges page: composes the stats bar,
 * filters and challenge cards, hydrating per-user score state on top of the
 * server-rendered challenge catalog.
 */
export function ChallengesView({ initialChallenges, initialProgress }: ChallengesViewProps) {
  const { user } = useAuth()
  const { score, completedChallenges, hydrate } = useScore()
  const {
    selectedLanguage,
    setSelectedLanguage,
    selectedDifficulty,
    setSelectedDifficulty,
    filteredChallenges,
    totalChallenges,
    totalPossiblePoints,
    resetFilters,
  } = useChallenges(initialChallenges)

  useEffect(() => {
    if (initialProgress) hydrate(initialProgress)
  }, [initialProgress, hydrate])

  return (
    <div className="mx-auto max-w-4xl px-4 py-8 lg:px-8">
      <ChallengeStats
        score={score}
        completedCount={completedChallenges.length}
        totalChallenges={totalChallenges}
        totalPossiblePoints={totalPossiblePoints}
      />

      <div className="mb-6">
        <h1 className="text-3xl font-bold text-foreground">Code Challenges</h1>
        <p className="mt-2 text-muted-foreground">
          Find the bug, fix the code and earn points. Use the Duck Mentor whenever you need help!
        </p>
        {!user && (
          <p className="mt-2 text-sm text-primary">
            You can try every challenge as a guest — sign in to save your score and earn points.
          </p>
        )}
      </div>

      <ChallengeFilters
        challenges={initialChallenges}
        selectedLanguage={selectedLanguage}
        onLanguageChange={setSelectedLanguage}
        selectedDifficulty={selectedDifficulty}
        onDifficultyChange={setSelectedDifficulty}
      />

      <div className="flex flex-col gap-6">
        {filteredChallenges.map((challenge) => (
          <ChallengeCard key={challenge.id} challenge={challenge} />
        ))}
      </div>

      {filteredChallenges.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <Code2 className="mb-4 h-12 w-12 text-muted-foreground" />
          <p className="text-lg text-muted-foreground">
            No challenges found with these filters.
          </p>
          <Button variant="outline" className="mt-4" onClick={resetFilters}>
            Clear Filters
          </Button>
        </div>
      )}
    </div>
  )
}
