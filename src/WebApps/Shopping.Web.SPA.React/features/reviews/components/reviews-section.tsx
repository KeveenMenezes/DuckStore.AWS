"use client"

import { useCallback, useEffect, useRef, useState, useTransition } from "react"
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
import { MyReviewCard } from "@/features/reviews/components/my-review-card"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { RatingHistogram } from "@/features/reviews/components/rating-histogram"
import { useProductRating } from "@/features/reviews/hooks/use-product-rating"
import { getMyReview, getReviewsByProduct } from "@/features/reviews/services/reviews.service"
import { useAuth } from "@/features/auth/hooks/use-auth"
import type { MyReview, Review } from "@/features/reviews/types/review.types"

interface ReviewsSectionProps {
  productId: string
  initialReviews: Review[]
  initialNextToken: string | null
}

export function ReviewsSection({ productId, initialReviews, initialNextToken }: ReviewsSectionProps) {
  const { summary, recordNewReview, recordReviewEdit, recordReviewRemoval } = useProductRating()
  const { user, isLoading: authLoading, loginWithCognito } = useAuth()
  // The customer's own row: undefined while loading, null when they never bought the product.
  // Fetched client-side because the page itself is ISR and identical for every visitor.
  const [myReview, setMyReview] = useState<MyReview | null | undefined>(undefined)
  const [editing, setEditing] = useState(false)
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

  useEffect(() => {
    if (!user) return
    let cancelled = false
    getMyReview(productId)
      .then((review) => {
        if (!cancelled) setMyReview(review)
      })
      .catch((error) => {
        console.error(`Failed to fetch my review for product ${productId}`, error)
        if (!cancelled) setMyReview(null)
      })
    return () => {
      cancelled = true
    }
  }, [productId, user])

  // Optimistic local list and summary (average/count/histogram) — the real values converge via
  // CDC on AWS (ReviewCreated/ReviewUpdated/ReviewDeleted → CatalogView).
  const handleReviewSubmitted = (result: { id: string; userName: string; rating: number; comment: string }) => {
    if (!myReview) return
    const published: MyReview = { ...myReview, ...result, status: "Published" }
    if (myReview.status === "Published") {
      setReviews((prev) => prev.map((r) => (r.id === published.id ? published : r)))
      recordReviewEdit(myReview.rating, published.rating)
    } else {
      setReviews((prev) => [published, ...prev.filter((r) => r.id !== published.id)])
      recordNewReview(published.rating)
    }
    setMyReview(published)
    setEditing(false)
  }

  const handleReviewDeleted = () => {
    if (!myReview) return
    setReviews((prev) => prev.filter((r) => r.id !== myReview.id))
    recordReviewRemoval(myReview.rating)
    setMyReview({ ...myReview, status: "Deleted" })
  }

  const renderMyReview = () => {
    if (authLoading) return null
    if (!user) {
      return (
        <div className="rounded-lg border border-border bg-secondary/40 p-4 text-center">
          <p className="text-sm text-muted-foreground">
            <button
              type="button"
              onClick={() => loginWithCognito(window.location.pathname)}
              className="font-medium text-foreground underline underline-offset-2"
            >
              Sign in
            </button>{" "}
            to leave a review.
          </p>
        </div>
      )
    }
    if (myReview === undefined) return null
    if (myReview === null) {
      return (
        <div className="rounded-lg border border-border bg-secondary/40 p-4 text-center">
          <p className="text-sm text-muted-foreground">Only customers who bought this product can review it.</p>
        </div>
      )
    }
    if (myReview.status === "Published" && !editing) {
      return <MyReviewCard review={myReview} onEdit={() => setEditing(true)} onDeleted={handleReviewDeleted} />
    }
    if (myReview.status === "Published") {
      return (
        <ReviewForm
          key="edit"
          productId={productId}
          mode="edit"
          initialRating={myReview.rating}
          initialComment={myReview.comment}
          onSubmitted={handleReviewSubmitted}
          onCancel={() => setEditing(false)}
        />
      )
    }
    // Eligible (bought, not reviewed yet) or Deleted (withdrawn, can publish again): empty form.
    return <ReviewForm key="create" productId={productId} onSubmitted={handleReviewSubmitted} />
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

      {renderMyReview()}

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
