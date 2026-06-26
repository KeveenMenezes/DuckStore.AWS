"use client"

import { useState } from "react"
import { CheckCircle2, XCircle, Lightbulb } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

interface ChallengeResultProps {
  isCorrect: boolean
  potentialPoints: number
  penalty: number
  currentHints: number
  explanation: string
}

export function ChallengeResult({
  isCorrect,
  potentialPoints,
  penalty,
  currentHints,
  explanation,
}: ChallengeResultProps) {
  const [showExplanation, setShowExplanation] = useState(false)

  return (
    <div className={cn(
      "rounded-lg border p-4",
      isCorrect
        ? "border-accent/30 bg-accent/10"
        : "border-destructive/30 bg-destructive/10"
    )}>
      <div className="flex items-center gap-2">
        {isCorrect ? (
          <>
            <CheckCircle2 className="h-5 w-5 text-accent" />
            <span className="font-semibold text-accent">Correct! +{potentialPoints} points</span>
          </>
        ) : (
          <>
            <XCircle className="h-5 w-5 text-destructive" />
            <span className="font-semibold text-destructive">Incorrect!</span>
          </>
        )}
      </div>
      {penalty > 0 && isCorrect && (
        <p className="mt-1 text-xs text-muted-foreground">
          (-{penalty} pts penalty for {currentHints} hint{currentHints > 1 ? "s" : ""} used)
        </p>
      )}
      <Button
        variant="ghost"
        size="sm"
        className="mt-2 gap-1 text-muted-foreground"
        onClick={() => setShowExplanation(!showExplanation)}
      >
        <Lightbulb className="h-4 w-4" />
        {showExplanation ? "Hide" : "Show"} explanation
      </Button>
      {showExplanation && (
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          {explanation}
        </p>
      )}
    </div>
  )
}
