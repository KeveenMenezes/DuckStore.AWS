"use client"

import { useState } from "react"
import { ReviewList } from "@/features/reviews/components/review-list"
import { ReviewForm } from "@/features/reviews/components/review-form"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
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
  const [nextToken] = useState<string | null>(initialNextToken)
  const [localCount, setLocalCount] = useState(ratingCount)
  const [localAvg, setLocalAvg] = useState(averageRating)

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
        productId={productId}
        initialReviews={reviews}
        initialNextToken={nextToken}
      />
    </div>
  )
}
