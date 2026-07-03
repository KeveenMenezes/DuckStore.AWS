"use client"

import { useState } from "react"
import { CheckCircle2, Lock } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { useScore } from "@/features/challenges/hooks/use-score"
import { DuckMentor } from "@/features/challenges/components/duck-mentor"
import { ChallengeOptions } from "@/features/challenges/components/challenge-options"
import { ChallengeResult } from "@/features/challenges/components/challenge-result"
import { difficultyConfig, languageColors, languageLabels } from "@/features/challenges/constants"
import type { Challenge } from "@/features/challenges/types/challenge.types"

export function ChallengeCard({ challenge }: { challenge: Challenge }) {
  const { completedChallenges, addScore, hintsUsed, spendHint, getHintPenalty } = useScore()
  const [selectedAnswer, setSelectedAnswer] = useState<number | null>(null)
  const [submitted, setSubmitted] = useState(false)

  const isCompleted = completedChallenges.includes(challenge.id)
  const isCorrect = selectedAnswer === challenge.correctAnswer
  const currentHints = hintsUsed[challenge.id] || 0
  const penalty = getHintPenalty(challenge.id)
  const potentialPoints = Math.max(0, challenge.points - penalty)
  const diff = difficultyConfig[challenge.difficulty]

  const handleSubmit = () => {
    if (selectedAnswer === null || submitted) return
    setSubmitted(true)
    if (selectedAnswer === challenge.correctAnswer) {
      addScore(challenge.id, challenge.points)
    }
  }

  const handleHint = () => {
    if (currentHints < challenge.hints.length) {
      spendHint(challenge.id)
    }
  }

  return (
    <Card className={cn(
      "border-border bg-card transition-all",
      isCompleted && "border-accent/30 bg-accent/5"
    )}>
      <CardHeader className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-2">
            <Badge className={cn("border text-xs", diff.className)}>
              {diff.label}
            </Badge>
            <Badge className={cn("text-xs", languageColors[challenge.language])}>
              {languageLabels[challenge.language]}
            </Badge>
            {isCompleted && (
              <Badge className="border-accent/30 bg-accent/20 text-xs text-accent">
                <CheckCircle2 className="mr-1 h-3 w-3" />
                Completed
              </Badge>
            )}
          </div>
          <CardTitle className="text-foreground">{challenge.title}</CardTitle>
          <p className="text-sm text-muted-foreground">{challenge.description}</p>
        </div>
        <div className="flex items-center gap-1 whitespace-nowrap rounded-lg bg-secondary px-3 py-1.5">
          <span className="text-sm font-bold text-primary">{isCompleted ? "---" : potentialPoints}</span>
          <span className="text-xs text-muted-foreground">pts</span>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {/* Code block */}
        <div className="overflow-x-auto rounded-lg border border-border bg-background p-4">
          <pre className="text-sm leading-relaxed">
            <code className="text-muted-foreground">
              {challenge.code}
            </code>
          </pre>
        </div>

        {/* Duck Mentor */}
        {!isCompleted && !submitted && (
          <DuckMentor
            hints={challenge.hints}
            currentHints={currentHints}
            onRequestHint={handleHint}
            penalty={penalty}
          />
        )}

        {/* Options */}
        <ChallengeOptions
          options={challenge.options}
          selectedAnswer={selectedAnswer}
          correctAnswer={challenge.correctAnswer}
          submitted={submitted}
          isCompleted={isCompleted}
          isCorrect={isCorrect}
          onSelect={setSelectedAnswer}
        />

        {/* Submit */}
        {!submitted && !isCompleted && (
          <Button
            className="gap-2"
            onClick={handleSubmit}
            disabled={selectedAnswer === null}
          >
            Check Answer
          </Button>
        )}

        {/* Result feedback */}
        {submitted && (
          <ChallengeResult
            isCorrect={isCorrect}
            potentialPoints={potentialPoints}
            penalty={penalty}
            currentHints={currentHints}
            explanation={challenge.explanation}
          />
        )}

        {isCompleted && !submitted && (
          <div className="flex items-center gap-2 rounded-lg border border-accent/30 bg-accent/10 p-3">
            <Lock className="h-4 w-4 text-accent" />
            <span className="text-sm text-accent">Challenge already completed!</span>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
