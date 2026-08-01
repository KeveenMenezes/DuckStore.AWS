"use client"

import { useState } from "react"
import { CheckCircle2, Lock, Loader2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { useScore } from "@/features/challenges/hooks/use-score"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { DuckMentor } from "@/features/challenges/components/duck-mentor"
import { ChallengeOptions } from "@/features/challenges/components/challenge-options"
import { ChallengeResult } from "@/features/challenges/components/challenge-result"
import { difficultyConfig, languageColors, languageLabels } from "@/features/challenges/constants"
import type { Challenge, SubmitAnswerResult } from "@/features/challenges/types/challenge.types"

export function ChallengeCard({ challenge }: { challenge: Challenge }) {
  const { user, loginWithCognito } = useAuth()
  const { answeredChallenges, hintsRevealed, revealedHints, getHintPenalty, submitAnswer, requestHint } =
    useScore()
  const [selectedAnswer, setSelectedAnswer] = useState<number | null>(null)
  const [result, setResult] = useState<SubmitAnswerResult | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [isRequestingHint, setIsRequestingHint] = useState(false)
  const [hintError, setHintError] = useState<string | null>(null)

  const isSignedIn = user !== null
  const priorAttempt = answeredChallenges[challenge.id] ?? null
  // Answering is one-shot, right or wrong (ADR-0045 §4): the server answers a second submission
  // with the first attempt verbatim, so the card must not offer one. Gating on "completed" alone
  // left the button live after a wrong answer, and the replay's verdict then painted whichever
  // option the customer had just picked.
  const isAnswered = priorAttempt !== null || result !== null
  const isCompleted = result?.isCorrect ?? priorAttempt?.isCorrect ?? false
  // The verdict on screen always belongs to the graded attempt, never to the local selection.
  const gradedOption = result?.selectedOption ?? priorAttempt?.selectedOption ?? null
  const currentHints = hintsRevealed[challenge.id] ?? 0
  const penalty = getHintPenalty(challenge.id)
  const potentialPoints = Math.max(0, challenge.points - penalty)
  const diff = difficultyConfig[challenge.difficulty]

  const handleSubmit = async () => {
    if (selectedAnswer === null || isSubmitting || isAnswered) return
    setIsSubmitting(true)
    setSubmitError(null)
    try {
      setResult(await submitAnswer(challenge.id, selectedAnswer))
    } catch (error) {
      console.error("Failed to submit challenge answer", error)
      setSubmitError("Couldn't check your answer right now. Please try again.")
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleHint = async () => {
    if (currentHints >= challenge.hintCount || isRequestingHint) return
    setIsRequestingHint(true)
    setHintError(null)
    try {
      await requestHint(challenge.id)
    } catch (error) {
      console.error("Failed to reveal hint", error)
      setHintError("Couldn't reveal a hint right now. Please try again.")
    } finally {
      setIsRequestingHint(false)
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
          <span className="text-sm font-bold text-primary">{isAnswered ? "---" : potentialPoints}</span>
          <span className="text-xs text-muted-foreground">pts</span>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <div className="overflow-x-auto rounded-lg border border-border bg-background p-4">
          <pre className="text-sm leading-relaxed">
            <code className="text-muted-foreground">
              {challenge.code}
            </code>
          </pre>
        </div>

        {!isAnswered && (
          <DuckMentor
            hintCount={challenge.hintCount}
            revealedHints={revealedHints[challenge.id] ?? []}
            currentHints={currentHints}
            onRequestHint={handleHint}
            isRequesting={isRequestingHint}
            error={hintError}
            penalty={penalty}
            disabled={!isSignedIn}
          />
        )}

        <ChallengeOptions
          options={challenge.options}
          selectedAnswer={isAnswered ? gradedOption : selectedAnswer}
          submitted={isAnswered}
          isCompleted={isCompleted}
          isCorrect={isCompleted}
          onSelect={setSelectedAnswer}
        />

        {!isSignedIn && !isAnswered && (
          <div className="flex flex-col items-start gap-2 rounded-lg border border-primary/30 bg-primary/5 p-3 sm:flex-row sm:items-center sm:justify-between">
            <span className="text-sm text-foreground">Sign in to check your answer and earn points.</span>
            <Button size="sm" variant="outline" onClick={() => loginWithCognito()}>
              Sign in
            </Button>
          </div>
        )}

        {isSignedIn && !isAnswered && (
          <Button
            className="gap-2"
            onClick={handleSubmit}
            disabled={selectedAnswer === null || isSubmitting}
          >
            {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
            Check Answer
          </Button>
        )}

        {submitError && <p className="text-sm text-destructive">{submitError}</p>}

        {result && (
          <ChallengeResult
            isCorrect={result.isCorrect}
            potentialPoints={result.pointsEarned}
            penalty={penalty}
            currentHints={currentHints}
            explanation={result.explanation}
          />
        )}

        {/* Answered in an earlier session: the verdict is known from the stored attempt, but the
            explanation isn't — myChallengeProgress carries attempts, not answer-key text. */}
        {priorAttempt && !result && (
          <div
            className={cn(
              "flex items-center gap-2 rounded-lg border p-3",
              priorAttempt.isCorrect
                ? "border-accent/30 bg-accent/10 text-accent"
                : "border-destructive/30 bg-destructive/10 text-destructive"
            )}
          >
            <Lock className="h-4 w-4" />
            <span className="text-sm">
              {priorAttempt.isCorrect
                ? "Challenge already completed!"
                : "You already answered this challenge — only the first attempt counts."}
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
