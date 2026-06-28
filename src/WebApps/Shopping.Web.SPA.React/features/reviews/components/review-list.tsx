"use client"

import { useState, useTransition } from "react"
import { Button } from "@/components/ui/button"
import { ReviewCard } from "@/features/reviews/components/review-card"
import { getReviewsByProduct } from "@/features/reviews/services/reviews.service"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewListProps {
  productId: string
  initialReviews: Review[]
  initialNextToken: string | null
}

export function ReviewList({ productId, initialReviews, initialNextToken }: ReviewListProps) {
  const [reviews, setReviews] = useState<Review[]>(initialReviews)
  const [nextToken, setNextToken] = useState<string | null>(initialNextToken)
  const [isPending, startTransition] = useTransition()

  const loadMore = () => {
    if (!nextToken) return
    startTransition(async () => {
      const page = await getReviewsByProduct(productId, 10, nextToken)
      setReviews((prev) => [...prev, ...page.items])
      setNextToken(page.nextToken)
    })
  }

  if (reviews.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-border py-12 text-center">
        <p className="text-sm text-muted-foreground">No reviews yet. Be the first to share your experience!</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      {reviews.map((review) => (
        <ReviewCard key={review.id} review={review} />
      ))}
      {nextToken && (
        <div className="mt-2 flex justify-center">
          <Button variant="outline" size="sm" onClick={loadMore} disabled={isPending}>
            {isPending ? "Loading..." : "Load more reviews"}
          </Button>
        </div>
      )}
    </div>
  )
}
