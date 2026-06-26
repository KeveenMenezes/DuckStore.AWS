"use client"

import Image from "next/image"
import { MessageCircle, AlertTriangle } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { HINT_PENALTY } from "@/features/challenges/constants"

interface DuckMentorProps {
  hints: string[]
  currentHints: number
  onRequestHint: () => void
  penalty: number
}

export function DuckMentor({ hints, currentHints, onRequestHint, penalty }: DuckMentorProps) {
  const hasMoreHints = currentHints < hints.length

  return (
    <div className="rounded-lg border border-primary/20 bg-primary/5 p-4">
      <div className="flex items-start gap-3">
        <div className="relative h-12 w-12 flex-shrink-0 overflow-hidden rounded-full border-2 border-primary/30">
          <Image
            src="/images/duck-mentor.jpg"
            alt="Duck Mentor"
            fill
            className="object-cover"
          />
        </div>
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <span className="font-semibold text-foreground">Duck Mentor</span>
            <span className="text-xs text-primary">Rubber Duck Debugging</span>
          </div>
          {currentHints === 0 ? (
            <p className="mt-1 text-sm text-muted-foreground">
              Need help? Explain the code to me and I'll give you a hint!
              Each hint costs {HINT_PENALTY} penalty points.
            </p>
          ) : (
            <div className="mt-2 flex flex-col gap-2">
              {hints.slice(0, currentHints).map((hint, index) => (
                <div
                  key={index}
                  className={cn(
                    "relative rounded-lg bg-card p-3 text-sm text-foreground",
                    "before:absolute before:-left-2 before:top-3 before:h-0 before:w-0",
                    "before:border-y-[6px] before:border-r-[8px] before:border-y-transparent before:border-r-card"
                  )}
                >
                  <span className="mb-1 block text-xs font-medium text-primary">
                    Hint {index + 1}:
                  </span>
                  {hint}
                </div>
              ))}
            </div>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {hasMoreHints ? (
              <Button
                variant="outline"
                size="sm"
                className="gap-1.5 border-primary/30 text-primary hover:bg-primary/10"
                onClick={onRequestHint}
              >
                <MessageCircle className="h-3.5 w-3.5" />
                Ask the duck for a hint ({currentHints}/{hints.length})
              </Button>
            ) : currentHints > 0 ? (
              <span className="text-xs text-muted-foreground">
                All hints have been revealed!
              </span>
            ) : null}
            {penalty > 0 && (
              <span className="flex items-center gap-1 text-xs text-destructive">
                <AlertTriangle className="h-3 w-3" />
                -{penalty} pts penalty
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
