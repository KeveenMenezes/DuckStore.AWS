"use client"

import { useEffect, useRef, useState } from "react"
import { ChevronDown, ChevronUp } from "lucide-react"
import { Card, CardContent } from "@/components/ui/card"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { formatReviewDate } from "@/features/reviews/services/reviews.service"
import { getInitials } from "@/shared/lib/format"
import type { Review } from "@/features/reviews/types/review.types"

export function ReviewCard({ review }: { review: Review }) {
  const [expanded, setExpanded] = useState(false)
  const [isClamped, setIsClamped] = useState(false)
  const commentRef = useRef<HTMLParagraphElement>(null)

  // "Show more" only appears when the clamped comment actually overflows.
  useEffect(() => {
    const el = commentRef.current
    if (!el) return
    setIsClamped(el.scrollHeight > el.clientHeight)
  }, [review.comment])

  return (
    <Card className="border-border bg-card">
      <CardContent className="p-4">
        <div className="flex items-start gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-secondary text-sm font-semibold text-foreground">
            {getInitials(review.userName)}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <p className="text-sm font-semibold text-foreground">{review.userName}</p>
            <div className="flex items-center gap-2">
              <StarRatingDisplay rating={review.rating} size="sm" />
              <span className="text-xs text-muted-foreground">{formatReviewDate(review.createdAt)}</span>
            </div>
            <p
              ref={commentRef}
              className={`text-sm text-muted-foreground leading-relaxed ${expanded ? "" : "line-clamp-3"}`}
            >
              {review.comment}
            </p>
            {(isClamped || expanded) && (
              <button
                type="button"
                onClick={() => setExpanded((prev) => !prev)}
                className="flex w-fit items-center gap-1 text-xs font-medium text-foreground transition-colors hover:text-muted-foreground"
              >
                {expanded ? (
                  <>
                    Show less
                    <ChevronUp className="h-3.5 w-3.5" />
                  </>
                ) : (
                  <>
                    Show more
                    <ChevronDown className="h-3.5 w-3.5" />
                  </>
                )}
              </button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
