"use client"

import { CheckCircle2, XCircle } from "lucide-react"
import { cn } from "@/lib/utils"

interface ChallengeOptionsProps {
  options: string[]
  selectedAnswer: number | null
  submitted: boolean
  isCompleted: boolean
  isCorrect: boolean
  onSelect: (index: number) => void
}

// No prop for the correct option's index — the server never reveals it, even when the
// submission was wrong (ADR-0045 §2, §10). Only the caller's own selection can be marked
// right/wrong; the explanation text (ChallengeResult) is where the fix is described in prose.
export function ChallengeOptions({
  options,
  selectedAnswer,
  submitted,
  isCompleted,
  isCorrect,
  onSelect,
}: ChallengeOptionsProps) {
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm font-medium text-foreground">Select the fix:</p>
      {options.map((option, index) => {
        let optionClass = "border-border bg-secondary/50 hover:bg-secondary hover:border-primary/30"
        if (submitted) {
          if (index === selectedAnswer) {
            optionClass = isCorrect
              ? "border-accent bg-accent/10"
              : "border-destructive bg-destructive/10"
          } else {
            optionClass = "border-border bg-secondary/30 opacity-50"
          }
        } else if (selectedAnswer === index) {
          optionClass = "border-primary bg-primary/10"
        }

        return (
          <button
            key={index}
            className={cn(
              "flex items-start gap-3 rounded-lg border p-3 text-left text-sm transition-all",
              optionClass,
              (isCompleted || submitted) && "cursor-default"
            )}
            onClick={() => {
              if (!submitted && !isCompleted) onSelect(index)
            }}
            disabled={submitted || isCompleted}
          >
            <span className={cn(
              "flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full border text-xs font-medium",
              selectedAnswer === index
                ? "border-primary bg-primary text-primary-foreground"
                : "border-border text-muted-foreground"
            )}>
              {String.fromCharCode(65 + index)}
            </span>
            <span className="text-foreground">{option}</span>
            {submitted && index === selectedAnswer && (
              isCorrect ? (
                <CheckCircle2 className="ml-auto h-5 w-5 flex-shrink-0 text-accent" />
              ) : (
                <XCircle className="ml-auto h-5 w-5 flex-shrink-0 text-destructive" />
              )
            )}
          </button>
        )
      })}
    </div>
  )
}
