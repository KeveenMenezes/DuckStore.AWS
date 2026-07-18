"use client"

import { useEffect, useState, useTransition } from "react"
import { ReviewList } from "@/features/reviews/components/review-list"
import { ReviewForm } from "@/features/reviews/components/review-form"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { getReviewsByProduct } from "@/features/reviews/services/reviews.service"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewsSectionProps {
  productId: string
  initialReviews: Review[]
  initialNextToken: string | null
  averageRating: number
  ratingCount: number
}

export function ReviewsSection({
  productId,
  initialReviews,
  initialNextToken,
  averageRating,
  ratingCount,
}: ReviewsSectionProps) {
  const [reviews, setReviews] = useState<Review[]>(initialReviews)
  const [nextToken, setNextToken] = useState<string | null>(initialNextToken)
  const [localCount, setLocalCount] = useState(ratingCount)
  const [localAvg, setLocalAvg] = useState(averageRating)
  const [isPending, startTransition] = useTransition()

  // The ISR-cached page can carry stale (or build-time-failed, hence empty)
  // reviews — refetch the first page in the browser so the list is always
  // current, keeping the server-rendered data as the instant first paint.
  useEffect(() => {
    let cancelled = false
    getReviewsByProduct(productId, 10)
      .then((page) => {
        if (cancelled) return
        setReviews(page.items)
        setNextToken(page.nextToken)
      })
      .catch(() => {}) // keep the server-rendered fallback
    return () => { cancelled = true }
  }, [productId])

  const loadMore = () => {
    if (!nextToken) return
    startTransition(async () => {
      const page = await getReviewsByProduct(productId, 10, nextToken)
      setReviews((prev) => [...prev, ...page.items])
      setNextToken(page.nextToken)
    })
  }

  const handleReviewCreated = (review: Review) => {
    setReviews((prev) => [review, ...prev])
    // Optimistic local average — real value converges via CDC on AWS
    const newCount = localCount + 1
    const newSum = localAvg * localCount + review.rating
    setLocalCount(newCount)
    setLocalAvg(newSum / newCount)
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-foreground">Customer Reviews</h2>
          <div className="mt-1">
            <StarRatingDisplay rating={localAvg} count={localCount} size="md" />
          </div>
        </div>
      </div>

      <ReviewForm productId={productId} onReviewCreated={handleReviewCreated} />

      <ReviewList
        reviews={reviews}
        nextToken={nextToken}
        isPending={isPending}
        onLoadMore={loadMore}
      />
    </div>
  )
}
