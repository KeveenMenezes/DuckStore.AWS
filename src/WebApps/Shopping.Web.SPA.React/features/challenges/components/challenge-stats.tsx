"use client"

import { Trophy, Target, Code2, Flame } from "lucide-react"
import { ALL_LANGUAGES } from "@/features/challenges/constants"

interface ChallengeStatsProps {
  score: number
  completedCount: number
  totalChallenges: number
  totalPossiblePoints: number
}

export function ChallengeStats({
  score,
  completedCount,
  totalChallenges,
  totalPossiblePoints,
}: ChallengeStatsProps) {
  const progress = totalChallenges > 0 ? Math.round((completedCount / totalChallenges) * 100) : 0

  return (
    <div className="mb-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
      <div className="rounded-lg border border-border bg-card p-4">
        <div className="flex items-center gap-2">
          <Trophy className="h-4 w-4 text-primary" />
          <span className="text-xs text-muted-foreground">Score</span>
        </div>
        <p className="mt-1 text-2xl font-bold text-foreground">{score}</p>
        <p className="text-xs text-muted-foreground">of {totalPossiblePoints} pts</p>
      </div>
      <div className="rounded-lg border border-border bg-card p-4">
        <div className="flex items-center gap-2">
          <Target className="h-4 w-4 text-accent" />
          <span className="text-xs text-muted-foreground">Completed</span>
        </div>
        <p className="mt-1 text-2xl font-bold text-foreground">{completedCount}</p>
        <p className="text-xs text-muted-foreground">of {totalChallenges} challenges</p>
      </div>
      <div className="rounded-lg border border-border bg-card p-4">
        <div className="flex items-center gap-2">
          <Code2 className="h-4 w-4 text-primary" />
          <span className="text-xs text-muted-foreground">Languages</span>
        </div>
        <p className="mt-1 text-2xl font-bold text-foreground">{ALL_LANGUAGES.length}</p>
        <p className="text-xs text-muted-foreground">available</p>
      </div>
      <div className="rounded-lg border border-border bg-card p-4">
        <div className="flex items-center gap-2">
          <Flame className="h-4 w-4 text-destructive" />
          <span className="text-xs text-muted-foreground">Progress</span>
        </div>
        <p className="mt-1 text-2xl font-bold text-foreground">{progress}%</p>
        <p className="text-xs text-muted-foreground">completed</p>
      </div>
    </div>
  )
}
