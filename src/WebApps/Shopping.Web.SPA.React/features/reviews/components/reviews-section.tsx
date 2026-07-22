"use client"

import { useCallback, useRef, useState, useTransition } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { ReviewList } from "@/features/reviews/components/review-list"
import { ReviewForm } from "@/features/reviews/components/review-form"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { RatingHistogram } from "@/features/reviews/components/rating-histogram"
import { useProductRating } from "@/features/reviews/hooks/use-product-rating"
import { getReviewsByProduct } from "@/features/reviews/services/reviews.service"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewsSectionProps {
  productId: string
  initialReviews: Review[]
  initialNextToken: string | null
}

export function ReviewsSection({ productId, initialReviews, initialNextToken }: ReviewsSectionProps) {
  const { summary, recordNewReview } = useProductRating()
  const [reviews, setReviews] = useState<Review[]>(initialReviews)
  const [nextToken, setNextToken] = useState<string | null>(initialNextToken)
  const [isPending, startTransition] = useTransition()
  const scrollContainerRef = useRef<HTMLDivElement>(null)

  const loadMore = useCallback(() => {
    if (!nextToken) return
    startTransition(async () => {
      const page = await getReviewsByProduct(productId, 10, nextToken)
      setReviews((prev) => [...prev, ...page.items])
      setNextToken(page.nextToken)
    })
  }, [productId, nextToken])

  const handleReviewCreated = (review: Review) => {
    setReviews((prev) => [review, ...prev])
    // Optimistic local summary (average/count/histogram) — real value converges via CDC on AWS.
    recordNewReview(review.rating)
  }

  return (
    <div className="flex flex-col gap-8">
      <div>
        <h2 className="mb-4 text-xl font-bold text-foreground">Customer Reviews</h2>
        <div className="flex flex-col items-center gap-6 sm:flex-row sm:items-center sm:gap-10">
          <div className="flex shrink-0 flex-col items-center gap-1">
            <span className="text-5xl font-bold text-foreground">{summary.averageRating.toFixed(1)}</span>
            <StarRatingDisplay rating={summary.averageRating} size="md" showRatingText={false} />
            <span className="text-sm text-muted-foreground">
              {summary.ratingCount} {summary.ratingCount === 1 ? "review" : "reviews"}
            </span>
          </div>
          <RatingHistogram
            ratingDistribution={summary.ratingDistribution}
            ratingCount={summary.ratingCount}
            className="w-full max-w-md"
          />
        </div>
      </div>

      <ReviewForm productId={productId} onReviewCreated={handleReviewCreated} />

      <div className="flex justify-center">
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="outline">See reviews</Button>
          </DialogTrigger>
          <DialogContent className="flex max-h-[85vh] flex-col sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>Customer Reviews</DialogTitle>
            </DialogHeader>

            <div ref={scrollContainerRef} className="-mx-6 flex-1 overflow-y-auto px-6">
              <ReviewList
                reviews={reviews}
                nextToken={nextToken}
                isPending={isPending}
                onLoadMore={loadMore}
                scrollContainerRef={scrollContainerRef}
              />
            </div>
          </DialogContent>
        </Dialog>
      </div>
    </div>
  )
}
