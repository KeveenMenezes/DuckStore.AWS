"use client"

import { useState, useTransition } from "react"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import { StarRatingInput } from "@/features/reviews/components/star-rating"
import { createReview } from "@/features/reviews/services/reviews.service"
import { useAuth } from "@/features/auth/hooks/use-auth"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewFormProps {
  productId: string
  onReviewCreated: (review: Review) => void
}

export function ReviewForm({ productId, onReviewCreated }: ReviewFormProps) {
  const { user } = useAuth()
  const [rating, setRating] = useState(0)
  const [comment, setComment] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [submitted, setSubmitted] = useState(false)
  const [isPending, startTransition] = useTransition()

  if (!user) {
    return (
      <div className="rounded-lg border border-border bg-secondary/40 p-4 text-center">
        <p className="text-sm text-muted-foreground">
          Sign in to leave a review.
        </p>
      </div>
    )
  }

  if (submitted) {
    return (
      <div className="rounded-lg border border-border bg-secondary/40 p-4 text-center">
        <p className="text-sm font-medium text-foreground">Thanks for your review!</p>
        <p className="mt-1 text-xs text-muted-foreground">Your feedback helps other customers.</p>
      </div>
    )
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (rating === 0) {
      setError("Please select a rating.")
      return
    }
    setError(null)
    startTransition(async () => {
      try {
        const id = await createReview({
          productId,
          userName: user.name,
          rating,
          comment,
        })
        onReviewCreated({
          id,
          productId,
          userName: user.name,
          rating,
          comment,
          createdAt: new Date().toISOString(),
        })
        // Trigger ISR revalidation so other users see the new review on next visit.
        const secret = process.env.NEXT_PUBLIC_REVIEW_WEBHOOK_SECRET
        if (secret) {
          fetch('/api/webhooks/review-created', {
            method: 'POST',
            headers: { 'x-webhook-secret': secret, 'content-type': 'application/json' },
            body: JSON.stringify({ productId }),
          }).catch(() => undefined)
        }
        setSubmitted(true)
      } catch {
        setError("Failed to submit review. Please try again.")
      }
    })
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold text-foreground">Write a Review</h3>

      <div className="flex flex-col gap-1.5">
        <Label className="text-xs text-muted-foreground">Your Rating</Label>
        <StarRatingInput value={rating} onChange={setRating} disabled={isPending} />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="review-comment" className="text-xs text-muted-foreground">
          Comment <span className="text-muted-foreground/60">(optional)</span>
        </Label>
        <Textarea
          id="review-comment"
          placeholder="Share your experience with this duck..."
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          disabled={isPending}
          className="min-h-[80px] resize-none text-sm"
          maxLength={500}
        />
      </div>

      {error && <p className="text-xs text-destructive">{error}</p>}

      <Button type="submit" size="sm" disabled={isPending} className="self-end">
        {isPending ? "Submitting..." : "Submit Review"}
      </Button>
    </form>
  )
}
