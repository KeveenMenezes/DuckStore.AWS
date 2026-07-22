"use client"

import { Star } from "lucide-react"
import { cn } from "@/lib/utils"

interface StarRatingDisplayProps {
  rating: number
  count?: number
  size?: "sm" | "md" | "lg"
  className?: string
  // Hides the inline "4.5" text next to the stars — for layouts (e.g. a large standalone
  // average number) that already render the numeric rating elsewhere.
  showRatingText?: boolean
}

export function StarRatingDisplay({
  rating,
  count,
  size = "md",
  className,
  showRatingText = true,
}: StarRatingDisplayProps) {
  const starSize = size === "sm" ? "h-3.5 w-3.5" : size === "lg" ? "h-6 w-6" : "h-4 w-4"
  const textSize = size === "sm" ? "text-xs" : size === "lg" ? "text-base" : "text-sm"

  return (
    <div className={cn("flex items-center gap-1.5", className)}>
      <div className="flex items-center gap-0.5">
        {Array.from({ length: 5 }, (_, i) => (
          <Star
            key={i}
            className={cn(
              starSize,
              i < Math.round(rating) ? "fill-primary text-primary" : "fill-muted text-muted-foreground/30",
            )}
          />
        ))}
      </div>
      {showRatingText && rating > 0 && (
        <span className={cn("font-medium text-foreground", textSize)}>{rating.toFixed(1)}</span>
      )}
      {count !== undefined && (
        <span className={cn("text-muted-foreground", textSize)}>
          ({count} {count === 1 ? "review" : "reviews"})
        </span>
      )}
    </div>
  )
}

interface StarRatingInputProps {
  value: number
  onChange: (value: number) => void
  disabled?: boolean
}

export function StarRatingInput({ value, onChange, disabled }: StarRatingInputProps) {
  return (
    <div className="flex items-center gap-1">
      {Array.from({ length: 5 }, (_, i) => {
        const starValue = i + 1
        return (
          <button
            key={i}
            type="button"
            disabled={disabled}
            onClick={() => onChange(starValue)}
            className={cn(
              "rounded transition-transform hover:scale-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              disabled && "cursor-not-allowed opacity-50",
            )}
            aria-label={`Rate ${starValue} star${starValue > 1 ? "s" : ""}`}
          >
            <Star
              className={cn(
                "h-7 w-7 transition-colors",
                starValue <= value ? "fill-primary text-primary" : "fill-muted text-muted-foreground/30 hover:fill-primary/40 hover:text-primary/60",
              )}
            />
          </button>
        )
      })}
    </div>
  )
}
