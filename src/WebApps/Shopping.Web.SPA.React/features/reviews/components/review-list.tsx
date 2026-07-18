"use client"

import { Button } from "@/components/ui/button"
import { ReviewCard } from "@/features/reviews/components/review-card"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewListProps {
  reviews: Review[]
  nextToken: string | null
  isPending: boolean
  onLoadMore: () => void
}

export function ReviewList({ reviews, nextToken, isPending, onLoadMore }: ReviewListProps) {
  // Only reviews with a written comment are shown — rating-only rows still
  // count toward the aggregate but add nothing to the list.
  const withComment = reviews.filter((review) => review.comment?.trim())

  if (withComment.length === 0 && !nextToken) {
    return (
      <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-border py-12 text-center">
        <p className="text-sm text-muted-foreground">No reviews yet. Be the first to share your experience!</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      {withComment.map((review) => (
        <ReviewCard key={review.id} review={review} />
      ))}
      {nextToken && (
        <div className="mt-2 flex justify-center">
          <Button variant="outline" size="sm" onClick={onLoadMore} disabled={isPending}>
            {isPending ? "Loading..." : "Load more reviews"}
          </Button>
        </div>
      )}
    </div>
  )
}
